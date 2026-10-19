using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KksDllStringExtractor;

// Recovers finite UI labels produced by a narrow static LINQ projection:
// a static string array is mapped through a lambda that concatenates a captured
// string and a literal delimiter with each array value (for example, "Bra: On").
// This inspects metadata and IL only; it never loads or executes plug-in code.
internal static class StaticStringProjection
{
    public static IEnumerable<RawRow> Read(TypeDefinition owner, string dll)
    {
        var cctor = owner.Methods.FirstOrDefault(m => m.IsConstructor && m.IsStatic && m.HasBody);
        if (cctor == null) yield break;

        var arrays = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dictionaries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        ReadStaticCollections(cctor, arrays, dictionaries);
        if (arrays.Count == 0 || dictionaries.Count == 0) yield break;

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in owner.Methods.Where(m => m.HasBody))
        {
            var instructions = GetCode(method);
            for (int selectIndex = 0; selectIndex < instructions.Count; selectIndex++)
            {
                if (!IsSelect(instructions[selectIndex])) continue;
                int lambdaIndex = FindLambda(instructions, selectIndex);
                if (lambdaIndex < 0 || instructions[lambdaIndex].Operand is not MethodReference lambdaReference)
                    continue;
                var lambda = Resolve(owner.Module, lambdaReference);
                if (lambda == null || !IsGuiContentFactory(lambda)) continue;

                string? arrayField = FindArrayField(instructions, lambdaIndex, arrays);
                if (arrayField == null) continue;

                foreach (var concat in FindLabelConcats(lambda))
                {
                    var staticValues = FindCapturedValues(instructions, lambdaIndex, concat.CapturedField, dictionaries);
                    if (staticValues == null) continue;
                    foreach (string prefix in staticValues)
                    foreach (string value in arrays[arrayField])
                    {
                        string combined = prefix + concat.Delimiter + value;
                        if (combined.Length == 0 || !emitted.Add(combined)) continue;
                        yield return new RawRow(dll, "STATIC_COMPOSITE", owner.FullName, lambda.FullName,
                            $"IL_{concat.Offset:X4}", combined, "DirectUI",
                            "System.String::Concat (static UI projection)");
                    }
                }
            }
        }
    }

    static void ReadStaticCollections(MethodDefinition cctor,
        Dictionary<string, List<string>> arrays, Dictionary<string, List<string>> dictionaries)
    {
        var instructions = GetCode(cctor);
        // A conditional initializer needs control-flow analysis; do not merge its branches.
        if (instructions.Any(i => i.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)) return;
        for (int storeIndex = 0; storeIndex < instructions.Count; storeIndex++)
        {
            if (instructions[storeIndex].OpCode != OpCodes.Stsfld || instructions[storeIndex].Operand is not FieldReference field)
                continue;

            if (field.FieldType is ArrayType array && array.ElementType.FullName == "System.String")
            {
                // Forget the old value even when the replacement cannot be understood.
                var values = ReadArrayValue(instructions, storeIndex, arrays);
                arrays.Remove(field.FullName);
                if (values != null) arrays[field.FullName] = values;
                continue;
            }

            if (!IsStringDictionary(field.FieldType)) continue;
            var dictionaryValues = ReadDictionaryValue(instructions, storeIndex, field, dictionaries);
            dictionaries.Remove(field.FullName);
            if (dictionaryValues != null) dictionaries[field.FullName] = dictionaryValues;
        }
    }

    static List<string>? ReadAlias(IReadOnlyList<Instruction> instructions, int storeIndex,
        Dictionary<string, List<string>> known)
    {
        return storeIndex > 0 && instructions[storeIndex - 1].OpCode == OpCodes.Ldsfld &&
            instructions[storeIndex - 1].Operand is FieldReference source &&
            known.TryGetValue(source.FullName, out var values) ? new List<string>(values) : null;
    }

    static List<string>? ReadArrayValue(IReadOnlyList<Instruction> instructions, int storeIndex,
        Dictionary<string, List<string>> arrays)
    {
        var alias = ReadAlias(instructions, storeIndex, arrays);
        if (alias != null) return alias;
        var indexed = new SortedDictionary<int, string>();
        int i = storeIndex - 1;
        while (i >= 3 && instructions[i].OpCode == OpCodes.Stelem_Ref)
        {
            if (instructions[i - 1].OpCode != OpCodes.Ldstr || instructions[i - 1].Operand is not string value ||
                !TryReadInt32(instructions[i - 2], out int index) || instructions[i - 3].OpCode != OpCodes.Dup ||
                index < 0) return null;
            // Walking backwards: the last assignment to an element wins.
            indexed.TryAdd(index, value);
            i -= 4;
        }
        if (i < 1 || instructions[i].OpCode != OpCodes.Newarr ||
            instructions[i].Operand is not TypeReference element || element.FullName != "System.String" ||
            !TryReadInt32(instructions[i - 1], out int length) || length != indexed.Count ||
            indexed.Keys.Where((index, position) => index != position).Any()) return null;
        return indexed.Values.ToList();
    }

    static List<string>? ReadDictionaryValue(IReadOnlyList<Instruction> instructions, int storeIndex,
        FieldReference field, Dictionary<string, List<string>> dictionaries)
    {
        var alias = ReadAlias(instructions, storeIndex, dictionaries);
        if (alias != null) return alias;
        var values = new List<string>();
        int i = storeIndex - 1;
        while (i >= 3 && IsCall(instructions[i]) && instructions[i].Operand is MethodReference add &&
            add.Name == "Add" && add.Parameters.Count == 2 && add.DeclaringType.FullName == field.FieldType.FullName)
        {
            if (instructions[i - 1].OpCode != OpCodes.Ldstr || instructions[i - 1].Operand is not string value ||
                !IsSimpleLoad(instructions[i - 2]) || instructions[i - 3].OpCode != OpCodes.Dup) return null;
            values.Add(value);
            i -= 4;
        }
        if (i < 0 || instructions[i].OpCode != OpCodes.Newobj ||
            instructions[i].Operand is not MethodReference constructor || constructor.Name != ".ctor" ||
            constructor.Parameters.Count != 0 || constructor.DeclaringType.FullName != field.FieldType.FullName)
            return null;
        values.Reverse();
        return values.Distinct(StringComparer.Ordinal).ToList();
    }

    static List<Instruction> GetCode(MethodDefinition method) =>
        method.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToList();

    static bool IsCall(Instruction instruction) =>
        instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt;

    static bool IsSimpleLoad(Instruction instruction) =>
        instruction.OpCode.Code is Code.Ldarg or Code.Ldarg_S or Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or
            Code.Ldloc or Code.Ldloc_S or Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or Code.Ldstr ||
        TryReadInt32(instruction, out _);

    static bool IsStringDictionary(TypeReference type) => type is GenericInstanceType generic &&
        generic.ElementType.FullName == "System.Collections.Generic.Dictionary`2" &&
        generic.GenericArguments.Count == 2 && generic.GenericArguments[1].FullName == "System.String";

    static bool IsSelect(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
        instruction.Operand is MethodReference method && method.Name == "Select" &&
        method.DeclaringType.FullName == "System.Linq.Enumerable";

    static int FindLambda(IReadOnlyList<Instruction> instructions, int selectIndex)
    {
        // Match the actual Select arguments, not an unrelated nearby delegate.
        return selectIndex >= 4 && instructions[selectIndex - 1].OpCode == OpCodes.Newobj &&
            instructions[selectIndex - 1].Operand is MethodReference constructor &&
            constructor.DeclaringType is GenericInstanceType func && func.ElementType.FullName == "System.Func`2" &&
            func.GenericArguments[0].FullName == "System.String" &&
            func.GenericArguments[1].FullName == "UnityEngine.GUIContent" &&
            instructions[selectIndex - 2].OpCode == OpCodes.Ldftn &&
            IsSimpleLoad(instructions[selectIndex - 3]) ? selectIndex - 2 : -1;
    }

    static string? FindArrayField(IReadOnlyList<Instruction> instructions, int lambdaIndex,
        Dictionary<string, List<string>> arrays)
    {
        return lambdaIndex >= 2 && instructions[lambdaIndex - 2].OpCode == OpCodes.Ldsfld &&
            instructions[lambdaIndex - 2].Operand is FieldReference field && arrays.ContainsKey(field.FullName)
            ? field.FullName : null;
    }

    static MethodDefinition? Resolve(ModuleDefinition module, MethodReference method)
    {
        if (method is MethodDefinition definition) return definition;
        string declaringType = method.DeclaringType.FullName;
        var type = Flatten(module.Types).FirstOrDefault(t => t.FullName == declaringType);
        if (type == null) return null;
        return type.Methods.FirstOrDefault(candidate => candidate.Name == method.Name &&
            candidate.Parameters.Count == method.Parameters.Count &&
            candidate.Parameters.Select(p => p.ParameterType.FullName)
                .SequenceEqual(method.Parameters.Select(p => p.ParameterType.FullName), StringComparer.Ordinal));
    }

    static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var child in Flatten(type.NestedTypes)) yield return child;
        }
    }

    static bool IsGuiContentFactory(MethodDefinition lambda)
    {
        if (lambda.ReturnType.FullName != "UnityEngine.GUIContent" || !lambda.HasBody ||
            !lambda.HasThis || lambda.Parameters.Count != 1 || lambda.Parameters[0].ParameterType.FullName != "System.String")
            return false;
        return lambda.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj &&
            i.Operand is MethodReference constructor && constructor.Name == ".ctor" &&
            constructor.DeclaringType.FullName == "UnityEngine.GUIContent" &&
            constructor.Parameters.Any(p => p.ParameterType.FullName == "System.String"));
    }

    static IEnumerable<(string CapturedField, string Delimiter, int Offset)> FindLabelConcats(MethodDefinition lambda)
    {
        var instructions = GetCode(lambda);
        for (int i = 4; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodes.Call || instructions[i].Operand is not MethodReference concat ||
                concat.DeclaringType.FullName != "System.String" || concat.Name != "Concat" ||
                concat.Parameters.Count != 3 || concat.Parameters.Any(p => p.ParameterType.FullName != "System.String") ||
                instructions[i - 3].OpCode != OpCodes.Ldfld || instructions[i - 3].Operand is not FieldReference captured ||
                captured.FieldType.FullName != "System.String" ||
                instructions[i - 2].OpCode != OpCodes.Ldstr || instructions[i - 2].Operand is not string delimiter ||
                !IsStringParameterLoad(instructions[i - 1], lambda) ||
                instructions[i - 4].OpCode != OpCodes.Ldarg_0 ||
                i + 2 >= instructions.Count || instructions[i + 1].OpCode != OpCodes.Newobj ||
                instructions[i + 1].Operand is not MethodReference constructor ||
                constructor.DeclaringType.FullName != "UnityEngine.GUIContent" || constructor.Name != ".ctor" ||
                constructor.Parameters.Count != 1 || constructor.Parameters[0].ParameterType.FullName != "System.String" ||
                instructions[i + 2].OpCode != OpCodes.Ret)
                continue;
            yield return (captured.FullName, delimiter, instructions[i].Offset);
        }
    }

    static bool IsStringParameterLoad(Instruction instruction, MethodDefinition method)
    {
        int argumentIndex;
        if (instruction.OpCode == OpCodes.Ldarg_0) argumentIndex = 0;
        else if (instruction.OpCode == OpCodes.Ldarg_1) argumentIndex = 1;
        else if (instruction.OpCode == OpCodes.Ldarg_2) argumentIndex = 2;
        else if (instruction.OpCode == OpCodes.Ldarg_3) argumentIndex = 3;
        else if (instruction.OpCode == OpCodes.Ldarg || instruction.OpCode == OpCodes.Ldarg_S)
        {
            if (instruction.Operand is not ParameterReference parameter) return false;
            return parameter.ParameterType.FullName == "System.String" &&
                parameter.Index == method.Parameters.Count - 1;
        }
        else return false;

        int parameterIndex = argumentIndex - (method.HasThis ? 1 : 0);
        return parameterIndex >= 0 && parameterIndex == method.Parameters.Count - 1 &&
            method.Parameters[parameterIndex].ParameterType.FullName == "System.String";
    }

    static List<string>? FindCapturedValues(IReadOnlyList<Instruction> instructions, int lambdaIndex,
        string capturedField, Dictionary<string, List<string>> dictionaries)
    {
        if (instructions.Any(i => i.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)) return null;
        // Later writes invalidate older dictionary evidence, including writes after
        // delegate creation: Select can defer evaluation until after that write.
        for (int storeIndex = instructions.Count - 1; storeIndex >= 0; storeIndex--)
        {
            if (instructions[storeIndex].OpCode != OpCodes.Stfld ||
                instructions[storeIndex].Operand is not FieldReference target || target.FullName != capturedField)
                continue;

            if (storeIndex >= lambdaIndex || storeIndex < 2) return null;
            if (instructions[storeIndex - 1].OpCode == OpCodes.Ldstr &&
                instructions[storeIndex - 1].Operand is string literal)
                return SameLoad(instructions[storeIndex - 2], instructions[lambdaIndex - 1]) ? new List<string> { literal } : null;

            if (storeIndex < 4 || !IsCall(instructions[storeIndex - 1]) ||
                instructions[storeIndex - 1].Operand is not MethodReference getter || getter.Name != "get_Item" ||
                getter.Parameters.Count != 1 || !IsStringDictionary(getter.DeclaringType) ||
                !IsSimpleLoad(instructions[storeIndex - 2]) ||
                instructions[storeIndex - 3].OpCode != OpCodes.Ldsfld ||
                instructions[storeIndex - 3].Operand is not FieldReference dictionaryField ||
                getter.DeclaringType.FullName != dictionaryField.FieldType.FullName ||
                !SameLoad(instructions[storeIndex - 4], instructions[lambdaIndex - 1])) return null;
            return dictionaries.TryGetValue(dictionaryField.FullName, out var values) ? values : null;
        }
        return null;
    }

    static bool SameLoad(Instruction first, Instruction second) =>
        IsSimpleLoad(first) && first.OpCode == second.OpCode && Equals(first.Operand, second.Operand);

    static bool TryReadInt32(Instruction instruction, out int value)
    {
        if (instruction.OpCode == OpCodes.Ldc_I4_M1) { value = -1; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_0) { value = 0; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_1) { value = 1; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_2) { value = 2; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_3) { value = 3; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_4) { value = 4; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_5) { value = 5; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_6) { value = 6; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_7) { value = 7; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_8) { value = 8; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4 && instruction.Operand is int intValue)
        { value = intValue; return true; }
        if (instruction.OpCode == OpCodes.Ldc_I4_S && instruction.Operand is sbyte shortValue)
        { value = shortValue; return true; }
        value = 0;
        return false;
    }
}
