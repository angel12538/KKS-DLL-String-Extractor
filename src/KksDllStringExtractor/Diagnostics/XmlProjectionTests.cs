using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KksDllStringExtractor;

internal static class XmlProjectionTests
{
    // A generated metadata/IL fixture, never loaded into the CLR or executed.
    public static AssemblyDefinition Fixture(bool overwriteName)
    {
        var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("XmlFixture", new Version(1, 0)), "XmlFixture", ModuleKind.Dll);
        var module = assembly.MainModule;
        TypeReference Ref(string ns, string name) => new(ns, name, module, module.TypeSystem.Object.Scope);
        var enumType = new TypeDefinition("Fixture", "Kind", TypeAttributes.Public | TypeAttributes.Sealed, Ref("System", "Enum"));
        module.Types.Add(enumType);
        enumType.Fields.Add(new FieldDefinition("value__", FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName, module.TypeSystem.Int32));
        foreach (var item in new[] { ("good", 0), ("inactive", 1) })
            enumType.Fields.Add(new FieldDefinition(item.Item1, FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, enumType) { Constant = item.Item2 });
        var record = new TypeDefinition("Fixture", "Record", TypeAttributes.Public | TypeAttributes.Class, module.TypeSystem.Object);
        module.Types.Add(record);
        var key = new FieldDefinition("Key", FieldAttributes.Public, enumType);
        var title = new FieldDefinition("Title", FieldAttributes.Public, module.TypeSystem.String);
        var active = new FieldDefinition("Active", FieldAttributes.Public, module.TypeSystem.Boolean);
        record.Fields.Add(key); record.Fields.Add(title); record.Fields.Add(active);
        var setRef = Ref("System.Collections.Generic", "HashSet`1");
        var set = new GenericInstanceType(setRef);
        set.GenericArguments.Add(enumType);
        var choices = new FieldDefinition("Choices", FieldAttributes.Static | FieldAttributes.InitOnly, set);
        record.Fields.Add(choices);
        var setCtor = new MethodReference(".ctor", module.TypeSystem.Void, set) { HasThis = true };
        var add = new MethodReference("Add", module.TypeSystem.Boolean, set) { HasThis = true };
        add.Parameters.Add(new ParameterDefinition(enumType));
        var contains = new MethodReference("Contains", module.TypeSystem.Boolean, set) { HasThis = true };
        contains.Parameters.Add(new ParameterDefinition(enumType));
        var cctor = new MethodDefinition(".cctor", MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        record.Methods.Add(cctor);
        var code = cctor.Body.GetILProcessor();
        code.Emit(OpCodes.Newobj, setCtor);
        code.Emit(OpCodes.Dup);
        code.Emit(OpCodes.Ldc_I4_0);
        code.Emit(OpCodes.Callvirt, add);
        code.Emit(OpCodes.Pop);
        code.Emit(OpCodes.Stsfld, choices);
        code.Emit(OpCodes.Ret);
        var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, module.TypeSystem.Void);
        record.Methods.Add(ctor);
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        ctor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var systemType = Ref("System", "Type");
        var getType = new MethodReference("GetTypeFromHandle", systemType, systemType);
        getType.Parameters.Add(new ParameterDefinition(Ref("System", "RuntimeTypeHandle")));
        var parse = new MethodReference("Parse", module.TypeSystem.Object, enumType.BaseType);
        parse.Parameters.Add(new ParameterDefinition(systemType));
        parse.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var objectCtor = new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true };
        code = ctor.Body.GetILProcessor();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Call, objectCtor);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldtoken, enumType);
        code.Emit(OpCodes.Call, getType);
        code.Emit(OpCodes.Ldarg_1);
        code.Emit(OpCodes.Call, parse);
        code.Emit(OpCodes.Unbox_Any, enumType);
        code.Emit(OpCodes.Stfld, key);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldsfld, choices);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, key);
        code.Emit(OpCodes.Callvirt, contains);
        code.Emit(OpCodes.Stfld, active);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldarg_2);
        code.Emit(OpCodes.Stfld, title);
        if (overwriteName)
        {
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Ldstr, "Replacement");
            code.Emit(OpCodes.Stfld, title);
        }
        code.Emit(OpCodes.Ret);
        var display = new MethodDefinition("Display", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
        display.Parameters.Add(new ParameterDefinition(record));
        record.Methods.Add(display);
        var prefix = Instruction.Create(OpCodes.Ldstr, "[+] ");
        var concat = new MethodReference("Concat", module.TypeSystem.String, module.TypeSystem.String);
        concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        concat.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        code = display.Body.GetILProcessor();
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, active);
        code.Emit(OpCodes.Brtrue_S, prefix);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, title);
        code.Emit(OpCodes.Ret);
        code.Append(prefix);
        code.Emit(OpCodes.Ldarg_0);
        code.Emit(OpCodes.Ldfld, title);
        code.Emit(OpCodes.Call, concat);
        code.Emit(OpCodes.Ret);
        var element = Ref("System.Xml.Linq", "XElement");
        var container = Ref("System.Xml.Linq", "XContainer");
        var name = Ref("System.Xml.Linq", "XName");
        var convert = new MethodReference("op_Implicit", name, name);
        convert.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var find = new MethodReference("Element", element, container) { HasThis = true };
        find.Parameters.Add(new ParameterDefinition(name));
        var value = new MethodReference("get_Value", module.TypeSystem.String, element) { HasThis = true };
        var load = new MethodDefinition("Load", MethodAttributes.Public | MethodAttributes.Static, record);
        load.Parameters.Add(new ParameterDefinition(element));
        record.Methods.Add(load);
        code = load.Body.GetILProcessor();
        foreach (string field in new[] { "Key", "Title" })
        {
            code.Emit(OpCodes.Ldarg_0);
            code.Emit(OpCodes.Ldstr, field);
            code.Emit(OpCodes.Call, convert);
            code.Emit(OpCodes.Callvirt, find);
            code.Emit(OpCodes.Callvirt, value);
        }
        code.Emit(OpCodes.Newobj, ctor);
        code.Emit(OpCodes.Ret);
        return assembly;
    }
}
