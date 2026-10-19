using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KksDllStringExtractor;

internal static class StaticProjectionTests
{
    public static void Run()
    {
        foreach (string scenario in new[] { "Normal", "ArrayAlias", "UnknownArray", "UnknownDictionary", "LiteralCapture",
                     "UnknownCapture", "DeferredCapture", "LogOnly" })
        {
            using var fixture = CreateFixture(scenario);
            using var bytes = new MemoryStream();
            fixture.Write(bytes);
            bytes.Position = 0;
            using var assembly = AssemblyDefinition.ReadAssembly(bytes);
            var owner = assembly.MainModule.Types.Single(t => t.Name == "Window");
            var rows = StaticStringProjection.Read(owner, "ProjectionFixture.dll").ToList();
            var expected = scenario switch
            {
                "Normal" => new[] { "Bra: On", "Underwear: On", "Legwear: On", "Bra: Off", "Underwear: Off", "Legwear: Off" },
                "ArrayAlias" => new[] { "Bra: Off", "Underwear: Off", "Legwear: Off" },
                "LiteralCapture" => new[] { "Fixed: On", "Fixed: Off" },
                _ => Array.Empty<string>()
            };
            if (!rows.Select(r => r.Text).ToHashSet(StringComparer.Ordinal).SetEquals(expected) || rows.Count != expected.Length)
                throw new InvalidOperationException("Projection regression failed: " + scenario);
            if (rows.Any(r => r.Kind != "STATIC_COMPOSITE" || r.Usage != "DirectUI" || !Classifier.Analyze(r).IsUi))
                throw new InvalidOperationException("Projection UI classification failed: " + scenario);
        }
        Console.WriteLine("PASS: static UI projections, collection aliases/unknown writes, captured-value replacements, deferred writes, log-only concatenations.");
    }

    // Generate IL only. The fixture is never loaded or executed, just like scanned plug-ins.
    static AssemblyDefinition CreateFixture(string scenario)
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("ProjectionFixture", new Version(1, 0)),
            "ProjectionFixture", ModuleKind.Dll);
        var module = assembly.MainModule;
        var owner = new TypeDefinition("Example", "Window", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(owner);
        var closure = new TypeDefinition("", "Closure", TypeAttributes.NestedPrivate | TypeAttributes.Class, module.TypeSystem.Object);
        owner.NestedTypes.Add(closure);
        var captured = new FieldDefinition("Name", FieldAttributes.Public, module.TypeSystem.String);
        closure.Fields.Add(captured);
        var scope = module.TypeSystem.Object.Scope;
        var gui = new TypeReference("UnityEngine", "GUIContent", module, scope);
        var guiCtor = new MethodReference(".ctor", module.TypeSystem.Void, gui) { HasThis = true };
        guiCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
        for (int i = 0; i < 3; i++) concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var lambda = new MethodDefinition("Build", MethodAttributes.Public, gui);
        lambda.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        closure.Methods.Add(lambda);
        var body = lambda.Body.GetILProcessor();
        body.Emit(OpCodes.Ldarg_0);
        body.Emit(OpCodes.Ldfld, captured);
        body.Emit(OpCodes.Ldstr, ": ");
        body.Emit(OpCodes.Ldarg_1);
        body.Emit(OpCodes.Call, concat);
        if (scenario == "LogOnly")
        {
            var log = new MethodDefinition("Log", MethodAttributes.Static | MethodAttributes.Private, module.TypeSystem.Void);
            log.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            log.Body.GetILProcessor().Emit(OpCodes.Ret);
            owner.Methods.Add(log);
            body.Emit(OpCodes.Call, log);
            body.Emit(OpCodes.Ldstr, "Fixed");
        }
        body.Emit(OpCodes.Newobj, guiCtor);
        body.Emit(OpCodes.Ret);

        var dictionaryType = new TypeReference("System.Collections.Generic", "Dictionary`2", module, scope);
        dictionaryType.GenericParameters.Add(new GenericParameter("TKey", dictionaryType));
        dictionaryType.GenericParameters.Add(new GenericParameter("TValue", dictionaryType));
        var dictionary = new GenericInstanceType(dictionaryType);
        dictionary.GenericArguments.Add(module.TypeSystem.Int32);
        dictionary.GenericArguments.Add(module.TypeSystem.String);
        var dictionaryCtor = new MethodReference(".ctor", module.TypeSystem.Void, dictionary) { HasThis = true };
        var add = new MethodReference("Add", module.TypeSystem.Void, dictionary) { HasThis = true };
        add.Parameters.Add(new ParameterDefinition(dictionaryType.GenericParameters[0]));
        add.Parameters.Add(new ParameterDefinition(dictionaryType.GenericParameters[1]));
        var getter = new MethodReference("get_Item", dictionaryType.GenericParameters[1], dictionary) { HasThis = true };
        getter.Parameters.Add(new ParameterDefinition(dictionaryType.GenericParameters[0]));
        var labels = new FieldDefinition("Labels", FieldAttributes.Static | FieldAttributes.InitOnly, dictionary);
        var states = new FieldDefinition("States", FieldAttributes.Static | FieldAttributes.InitOnly, new ArrayType(module.TypeSystem.String));
        var other = new FieldDefinition("Other", FieldAttributes.Static | FieldAttributes.InitOnly, states.FieldType);
        owner.Fields.Add(labels);
        owner.Fields.Add(states);
        owner.Fields.Add(other);
        var initializer = new MethodDefinition(".cctor", MethodAttributes.Static | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        owner.Methods.Add(initializer);
        var init = initializer.Body.GetILProcessor();
        init.Emit(OpCodes.Newobj, dictionaryCtor);
        var names = new[] { "Bra", "Underwear", "Legwear" };
        for (int i = 0; i < names.Length; i++)
        {
            init.Emit(OpCodes.Dup);
            init.Emit(OpCodes.Ldc_I4, i);
            init.Emit(OpCodes.Ldstr, names[i]);
            init.Emit(OpCodes.Callvirt, add);
        }
        init.Emit(OpCodes.Stsfld, labels);
        EmitArray(init, other, new[] { "Off" });
        EmitArray(init, states, new[] { "On", "Off" });
        if (scenario == "ArrayAlias")
        {
            init.Emit(OpCodes.Ldsfld, other);
            init.Emit(OpCodes.Stsfld, states);
        }
        if (scenario == "UnknownArray")
        {
            init.Emit(OpCodes.Ldnull);
            init.Emit(OpCodes.Stsfld, states);
        }
        if (scenario == "UnknownDictionary")
        {
            init.Emit(OpCodes.Ldnull);
            init.Emit(OpCodes.Stsfld, labels);
        }
        init.Emit(OpCodes.Ret);

        var objectCtor = new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true };
        var closureCtor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName |
            MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        closure.Methods.Add(closureCtor);
        var closureInit = closureCtor.Body.GetILProcessor();
        closureInit.Emit(OpCodes.Ldarg_0);
        closureInit.Emit(OpCodes.Call, objectCtor);
        closureInit.Emit(OpCodes.Ret);
        var funcType = new TypeReference("System", "Func`2", module, scope);
        var func = new GenericInstanceType(funcType);
        func.GenericArguments.Add(module.TypeSystem.String);
        func.GenericArguments.Add(gui);
        var funcCtor = new MethodReference(".ctor", module.TypeSystem.Void, func) { HasThis = true };
        funcCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
        funcCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        var enumerable = new TypeReference("System.Linq", "Enumerable", module, scope);
        var select = new MethodReference("Select", module.TypeSystem.Object, enumerable);
        select.Parameters.Add(new ParameterDefinition(states.FieldType));
        select.Parameters.Add(new ParameterDefinition(func));
        var create = new MethodDefinition("Create", MethodAttributes.Static | MethodAttributes.Public, module.TypeSystem.Void);
        owner.Methods.Add(create);
        create.Body.Variables.Add(new VariableDefinition(closure));
        create.Body.InitLocals = true;
        var caller = create.Body.GetILProcessor();
        caller.Emit(OpCodes.Newobj, closureCtor);
        caller.Emit(OpCodes.Stloc_0);
        caller.Emit(OpCodes.Ldloc_0);
        caller.Emit(OpCodes.Ldsfld, labels);
        caller.Emit(OpCodes.Ldc_I4_0);
        caller.Emit(OpCodes.Callvirt, getter);
        caller.Emit(OpCodes.Stfld, captured);
        if (scenario is "LiteralCapture" or "UnknownCapture")
        {
            caller.Emit(OpCodes.Ldloc_0);
            if (scenario == "LiteralCapture") caller.Emit(OpCodes.Ldstr, "Fixed");
            else caller.Emit(OpCodes.Ldnull);
            caller.Emit(OpCodes.Stfld, captured);
        }
        caller.Emit(OpCodes.Ldsfld, states);
        caller.Emit(OpCodes.Ldloc_0);
        caller.Emit(OpCodes.Ldftn, lambda);
        caller.Emit(OpCodes.Newobj, funcCtor);
        caller.Emit(OpCodes.Call, select);
        caller.Emit(OpCodes.Pop);
        if (scenario == "DeferredCapture")
        {
            caller.Emit(OpCodes.Ldloc_0);
            caller.Emit(OpCodes.Ldstr, "Later");
            caller.Emit(OpCodes.Stfld, captured);
        }
        caller.Emit(OpCodes.Ret);
        return assembly;
    }

    static void EmitArray(ILProcessor il, FieldReference field, string[] values)
    {
        il.Emit(OpCodes.Ldc_I4, values.Length);
        il.Emit(OpCodes.Newarr, field.Module.TypeSystem.String);
        for (int i = 0; i < values.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldstr, values[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }
        il.Emit(OpCodes.Stsfld, field);
    }
}
