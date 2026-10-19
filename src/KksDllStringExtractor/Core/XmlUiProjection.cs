using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Xml.Linq;

namespace KksDllStringExtractor;

// Recognizes an XML -> constructor -> fields -> conditional prefix pipeline.
// The condition must be an enum ID's membership in a literal static HashSet.
// No names, descriptions, plug-in identities or enum values are hardcoded.
internal static class XmlUiProjection
{
    public static IEnumerable<RawRow> Read(XDocument document, ModuleDefinition module, string dll, string resource)
    {
        var types = Flatten(module.Types).ToList();
        var methods = types.SelectMany(t => t.Methods).Where(m => m.HasBody).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var display in methods.Where(m => m.ReturnType.FullName == "System.String"))
        {
            var code = Instructions(display);
            for (int i = 0; i + 4 < code.Length; i++)
            {
                if (code[i].OpCode != OpCodes.Ldstr || code[i].Operand is not string prefix || prefix.Length == 0 ||
                    !IsArg(code[i + 1], display, 0) || code[i + 2].OpCode != OpCodes.Ldfld ||
                    code[i + 2].Operand is not FieldReference nameField ||
                    code[i + 3].OpCode != OpCodes.Call || code[i + 3].Operand is not MethodReference concat ||
                    concat.DeclaringType.FullName != "System.String" || concat.Name != "Concat" ||
                    concat.Parameters.Count != 2 || concat.Parameters.Any(p => p.ParameterType.FullName != "System.String") ||
                    code[i + 4].OpCode != OpCodes.Ret) continue;

                var nameType = types.FirstOrDefault(t => t.FullName == nameField.DeclaringType.FullName);
                if (nameType == null) continue;
                // Prefix branch: if (item.Flag) return prefix + item.Name.
                var flag = FindFlag(code, i, display, nameType);
                if (flag == null) continue;
                foreach (var constructor in nameType.Methods.Where(m => m.IsConstructor && !m.IsStatic && m.HasBody))
                {
                    var ctorCode = Instructions(constructor);
                    int nameArg = FindStringArgument(ctorCode, constructor, nameField.FullName);
                    if (nameArg < 0 || !FindMembership(ctorCode, flag.FullName, types, out var enumField, out var values)) continue;
                    var enumType = types.FirstOrDefault(t => t.FullName == enumField!.FieldType.FullName && t.IsEnum);
                    if (enumType == null) continue;
                    int idArg = FindEnumArgument(ctorCode, constructor, enumField!.FullName);
                    if (idArg < 0) continue;
                    var ids = enumType.Fields.Where(f => f.HasConstant && values!.Contains(Convert.ToInt64(f.Constant)))
                        .Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

                    foreach (var names in FindXmlBindings(methods, constructor))
                    {
                        if (idArg >= names.Length || nameArg >= names.Length) continue;
                        foreach (var record in document.Descendants())
                        {
                            var id = record.Element(names[idArg]);
                            var name = record.Element(names[nameArg]);
                            if (id == null || name == null || id.HasElements || name.HasElements || !ids.Contains(id.Value)) continue;
                            string text = prefix + name.Value;
                            if (seen.Add(text))
                                yield return new RawRow(dll, "RESOURCE_COMPOSITE", resource, display.FullName,
                                    $"IL_{code[i].Offset:X4}", text);
                        }
                    }
                }
            }
        }
    }

    static FieldReference? FindFlag(Instruction[] code, int prefixIndex, MethodDefinition method, TypeDefinition owner)
    {
        for (int i = Math.Max(2, prefixIndex - 8); i < prefixIndex; i++)
            if ((code[i].OpCode == OpCodes.Brtrue || code[i].OpCode == OpCodes.Brtrue_S) &&
                ReferenceEquals(code[i].Operand, code[prefixIndex]) &&
                code[i - 1].OpCode == OpCodes.Ldfld && code[i - 1].Operand is FieldReference flag &&
                flag.DeclaringType.FullName == owner.FullName && flag.FieldType.FullName == "System.Boolean" &&
                IsArg(code[i - 2], method, 0)) return flag;
        return null;
    }

    static int FindStringArgument(Instruction[] code, MethodDefinition method, string field)
    {
        if (CountWrites(code, field) != 1) return -1;
        for (int i = 1; i < code.Length; i++)
            if (code[i].OpCode == OpCodes.Stfld && code[i].Operand is FieldReference target && target.FullName == field)
                for (int p = 0; p < method.Parameters.Count; p++)
                    if (method.Parameters[p].ParameterType.FullName == "System.String" && IsArg(code[i - 1], method, p)) return p;
        return -1;
    }

    static int FindEnumArgument(Instruction[] code, MethodDefinition method, string field)
    {
        if (CountWrites(code, field) != 1) return -1;
        for (int i = 3; i < code.Length; i++)
            if (code[i].OpCode == OpCodes.Stfld && code[i].Operand is FieldReference target && target.FullName == field &&
                code[i - 1].OpCode == OpCodes.Unbox_Any && code[i - 2].OpCode == OpCodes.Call &&
                code[i - 2].Operand is MethodReference parse && parse.DeclaringType.FullName == "System.Enum" &&
                parse.Name == "Parse" && parse.Parameters.Count == 2)
                for (int p = 0; p < method.Parameters.Count; p++)
                    if (IsArg(code[i - 3], method, p)) return p;
        return -1;
    }

    static bool FindMembership(Instruction[] code, string flag, List<TypeDefinition> types,
        out FieldReference? enumField, out HashSet<long>? values)
    {
        enumField = null;
        values = null;
        if (CountWrites(code, flag) != 1) return false;
        for (int i = 5; i < code.Length; i++)
        {
            if (code[i].OpCode != OpCodes.Stfld || code[i].Operand is not FieldReference destination || destination.FullName != flag ||
                code[i - 1].OpCode != OpCodes.Callvirt || code[i - 1].Operand is not MethodReference contains || contains.Name != "Contains" ||
                contains.DeclaringType is not GenericInstanceType set || set.ElementType.FullName != "System.Collections.Generic.HashSet`1" ||
                code[i - 2].OpCode != OpCodes.Ldfld || code[i - 2].Operand is not FieldReference id ||
                code[i - 3].OpCode != OpCodes.Ldarg_0 || code[i - 4].OpCode != OpCodes.Ldsfld ||
                code[i - 4].Operand is not FieldReference setField || setField.FieldType.FullName != set.FullName ||
                set.GenericArguments[0].FullName != id.FieldType.FullName) continue;
            var owner = types.FirstOrDefault(t => t.FullName == setField.DeclaringType.FullName);
            var cctor = owner?.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody);
            if (cctor == null) continue;
            var initializer = Instructions(cctor);
            if (initializer.Any(ins => ins.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)) continue;
            for (int store = initializer.Length - 1; store >= 0; store--)
            {
                if (initializer[store].OpCode != OpCodes.Stsfld || initializer[store].Operand is not FieldReference field ||
                    field.FullName != setField.FullName) continue;
                var result = new HashSet<long>();
                int at = store - 1;
                while (at >= 3 && initializer[at].OpCode == OpCodes.Pop && initializer[at - 1].OpCode == OpCodes.Callvirt &&
                    initializer[at - 1].Operand is MethodReference add && add.Name == "Add" && add.DeclaringType.FullName == set.FullName &&
                    initializer[at - 3].OpCode == OpCodes.Dup && TryInt(initializer[at - 2], out long value))
                {
                    result.Add(value);
                    at -= 4;
                }
                if (at < 0 || initializer[at].OpCode != OpCodes.Newobj || initializer[at].Operand is not MethodReference ctor ||
                    ctor.DeclaringType.FullName != set.FullName || ctor.Parameters.Count != 0) return false;
                enumField = id;
                values = result;
                return true;
            }
        }
        return false;
    }

    static IEnumerable<string[]> FindXmlBindings(List<MethodDefinition> methods, MethodDefinition constructor)
    {
        // Exact XML string loads immediately precede this constructor call.
        foreach (var method in methods)
        {
            var code = Instructions(method);
            for (int i = 0; i < code.Length; i++)
            {
                if (code[i].OpCode != OpCodes.Newobj || code[i].Operand is not MethodReference ctor || ctor.FullName != constructor.FullName) continue;
                var names = new string[constructor.Parameters.Count];
                int at = i - 1;
                bool valid = true;
                for (int p = constructor.Parameters.Count - 1; p >= 0; p--)
                {
                    string type = constructor.Parameters[p].ParameterType.FullName;
                    if (type == "UnityEngine.AssetBundle" && at >= 0 && code[at].OpCode == OpCodes.Ldsfld)
                    { names[p] = ""; at--; continue; }
                    if (type != "System.String" || at < 4 || !IsArg(code[at - 4], method, 0) ||
                        code[at - 3].OpCode != OpCodes.Ldstr || code[at - 3].Operand is not string element ||
                        code[at - 2].OpCode != OpCodes.Call || code[at - 2].Operand is not MethodReference convert ||
                        convert.DeclaringType.FullName != "System.Xml.Linq.XName" || convert.Name != "op_Implicit" ||
                        code[at - 1].OpCode != OpCodes.Callvirt || code[at - 1].Operand is not MethodReference find ||
                        find.DeclaringType.FullName != "System.Xml.Linq.XContainer" || find.Name != "Element" ||
                        code[at].OpCode != OpCodes.Callvirt || code[at].Operand is not MethodReference value ||
                        value.DeclaringType.FullName != "System.Xml.Linq.XElement" || value.Name != "get_Value")
                    { valid = false; break; }
                    names[p] = element;
                    at -= 5;
                }
                if (valid) yield return names;
            }
        }
    }

    static int CountWrites(Instruction[] code, string field) => code.Count(i =>
        i.OpCode == OpCodes.Stfld && i.Operand is FieldReference target && target.FullName == field);

    static bool IsArg(Instruction instruction, MethodDefinition method, int parameter)
    {
        int index = parameter + (method.HasThis ? 1 : 0);
        if (instruction.OpCode.Code is >= Code.Ldarg_0 and <= Code.Ldarg_3)
            return instruction.OpCode.Code - Code.Ldarg_0 == index;
        return (instruction.OpCode == OpCodes.Ldarg || instruction.OpCode == OpCodes.Ldarg_S) &&
            instruction.Operand is ParameterReference argument && argument.Index == parameter;
    }

    static bool TryInt(Instruction instruction, out long value)
    {
        value = 0;
        if (instruction.OpCode.Code is >= Code.Ldc_I4_M1 and <= Code.Ldc_I4_8)
        { value = instruction.OpCode.Code - Code.Ldc_I4_0; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4 || instruction.OpCode == OpCodes.Ldc_I4_S)
        { value = Convert.ToInt64(instruction.Operand); return true; }
        return false;
    }

    static Instruction[] Instructions(MethodDefinition method) => method.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
    static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Flatten(type.NestedTypes)) yield return nested;
        }
    }
}
