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
            var instructions = method.Body.Instructions;
            for (int selectIndex = 0; selectIndex < instructions.Count; selectIndex++)
            {
                if (!IsSelect(instructions[selectIndex])) continue;
                int lambdaIndex = FindLambda(instructions, selectIndex);
                if (lambdaIndex < 0 || instructions[lambdaIndex].Operand is not MethodReference lambdaReference)
                    continue;
                var lambda = Resolve(owner.Module, lambdaReference);
                if (lambda == null || !IsGuiContentFactory(lambda)) continue;

                string? arrayField = FindArrayField(instructions, lambdaIndex, selectIndex, arrays);
                if (arrayField == null) continue;

                foreach (var concat in FindLabelConcats(lambda))
                {
                    var staticValues = FindCapturedDictionaryValues(instructions, concat.CapturedField, dictionaries);
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
        var instructions = cctor.Body.Instructions;
        for (int storeIndex = 0; storeIndex < instructions.Count; storeIndex++)
        {
            if (instructions[storeIndex].OpCode != OpCodes.Stsfld || instructions[storeIndex].Operand is not FieldReference field)
                continue;

            if (field.FieldType is ArrayType array && array.ElementType.FullName == "System.String")
            {
                int allocation = FindStringArrayAllocation(instructions, storeIndex);
                if (allocation < 0) continue;
                var indexed = new SortedDictionary<int, string>();
                for (int i = allocation + 1; i < storeIndex; i++)
                {
                    if (instructions[i].OpCode != OpCodes.Stelem_Ref || i < 3 ||
                        instructions[i - 1].OpCode != OpCodes.Ldstr || instructions[i - 1].Operand is not string value ||
                        !TryReadInt32(instructions[i - 2], out int elementIndex) ||
                        instructions[i - 3].OpCode != OpCodes.Dup)
                        continue;
                    indexed[elementIndex] = value;
                }
                if (indexed.Count > 0)
                    arrays[field.FullName] = indexed.Values.ToList();
                continue;
            }

            if (!IsStringDictionary(field.FieldType)) continue;
            int dictionaryAllocation = FindDictionaryAllocation(instructions, storeIndex, field.FieldType.FullName);
            if (dictionaryAllocation < 0) continue;
            var values = new List<string>();
            for (int i = dictionaryAllocation + 1; i < storeIndex; i++)
            {
                var op = instructions[i].OpCode;
                if (op != OpCodes.Call && op != OpCodes.Callvirt ||
                    instructions[i].Operand is not MethodReference add || add.Name != "Add" ||
                    add.Parameters.Count != 2 ||
                    add.DeclaringType.FullName != field.FieldType.FullName || i == 0 ||
                    instructions[i - 1].OpCode != OpCodes.Ldstr || instructions[i - 1].Operand is not string value)
                    continue;
                values.Add(value);
            }
            if (values.Count > 0)
                dictionaries[field.FullName] = values.Distinct(StringComparer.Ordinal).ToList();
        }
    }

    static int FindStringArrayAllocation(Mono.Collections.Generic.Collection<Instruction> instructions, int storeIndex)
    {
        for (int i = storeIndex - 1; i >= 0; i--)
            if (instructions[i].OpCode == OpCodes.Newarr && instructions[i].Operand is TypeReference element &&
                element.FullName == "System.String") return i;
        return -1;
    }

    static int FindDictionaryAllocation(Mono.Collections.Generic.Collection<Instruction> instructions,
        int storeIndex, string fieldType)
    {
        for (int i = storeIndex - 1; i >= 0; i--)
            if (instructions[i].OpCode == OpCodes.Newobj && instructions[i].Operand is MethodReference constructor &&
                constructor.Name == ".ctor" && constructor.DeclaringType.FullName == fieldType) return i;
        return -1;
    }

    static bool IsStringDictionary(TypeReference type) => type is GenericInstanceType generic &&
        generic.ElementType.FullName == "System.Collections.Generic.Dictionary`2" &&
        generic.GenericArguments.Count == 2 && generic.GenericArguments[1].FullName == "System.String";

    static bool IsSelect(Instruction instruction) =>
        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt) &&
        instruction.Operand is MethodReference method && method.Name == "Select" &&
        method.DeclaringType.FullName == "System.Linq.Enumerable";

    static int FindLambda(Mono.Collections.Generic.Collection<Instruction> instructions, int selectIndex)
    {
        int start = Math.Max(0, selectIndex - 12);
        for (int i = selectIndex - 1; i >= start; i--)
            if (instructions[i].OpCode == OpCodes.Ldftn && instructions[i].Operand is MethodReference)
                return i;
        return -1;
    }

    static string? FindArrayField(Mono.Collections.Generic.Collection<Instruction> instructions, int lambdaIndex,
        int selectIndex, Dictionary<string, List<string>> arrays)
    {
        int start = Math.Max(0, lambdaIndex - 24);
        for (int i = lambdaIndex - 1; i >= start && i < selectIndex; i--)
            if (instructions[i].OpCode == OpCodes.Ldsfld && instructions[i].Operand is FieldReference field &&
                arrays.ContainsKey(field.FullName)) return field.FullName;
        return null;
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
            lambda.Parameters.Count == 0 || lambda.Parameters[^1].ParameterType.FullName != "System.String")
            return false;
        return lambda.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj &&
            i.Operand is MethodReference constructor && constructor.Name == ".ctor" &&
            constructor.DeclaringType.FullName == "UnityEngine.GUIContent" &&
            constructor.Parameters.Any(p => p.ParameterType.FullName == "System.String"));
    }

    static IEnumerable<(string CapturedField, string Delimiter, int Offset)> FindLabelConcats(MethodDefinition lambda)
    {
        var instructions = lambda.Body.Instructions;
        for (int i = 4; i < instructions.Count; i++)
        {
            if (instructions[i].OpCode != OpCodes.Call || instructions[i].Operand is not MethodReference concat ||
                concat.DeclaringType.FullName != "System.String" || concat.Name != "Concat" ||
                concat.Parameters.Count != 3 || concat.Parameters.Any(p => p.ParameterType.FullName != "System.String") ||
                instructions[i - 3].OpCode != OpCodes.Ldfld || instructions[i - 3].Operand is not FieldReference captured ||
                captured.FieldType.FullName != "System.String" ||
                instructions[i - 2].OpCode != OpCodes.Ldstr || instructions[i - 2].Operand is not string delimiter ||
                !IsStringParameterLoad(instructions[i - 1], lambda))
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

    static List<string>? FindCapturedDictionaryValues(Mono.Collections.Generic.Collection<Instruction> instructions,
        string capturedField, Dictionary<string, List<string>> dictionaries)
    {
        for (int storeIndex = 0; storeIndex < instructions.Count; storeIndex++)
        {
            if (instructions[storeIndex].OpCode != OpCodes.Stfld ||
                instructions[storeIndex].Operand is not FieldReference target || target.FullName != capturedField)
                continue;

            int start = Math.Max(0, storeIndex - 18);
            for (int i = storeIndex - 1; i >= start; i--)
            {
                var op = instructions[i].OpCode;
                if (op != OpCodes.Call && op != OpCodes.Callvirt ||
                    instructions[i].Operand is not MethodReference getter || getter.Name != "get_Item" ||
                    getter.DeclaringType.FullName != target.DeclaringType.FullName &&
                    !getter.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2", StringComparison.Ordinal))
                    continue;

                for (int j = i - 1; j >= start; j--)
                    if (instructions[j].OpCode == OpCodes.Ldsfld && instructions[j].Operand is FieldReference dictionaryField &&
                        dictionaries.TryGetValue(dictionaryField.FullName, out var values)) return values;
            }
        }
        return null;
    }

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
