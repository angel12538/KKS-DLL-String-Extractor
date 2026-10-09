using Mono.Cecil;
using Mono.Cecil.Cil;

namespace KksDllStringExtractor;

// A conservative, nearby IL call-site hint. Does not execute plug-ins, does not require resolving dependencies.
// A nearby GUI-related method name alone is not evidence: only a relevant call is considered "DirectUI".
internal static class CallInspector
{
    public static (string Usage, string Target) Inspect(Mono.Collections.Generic.Collection<Instruction> instructions, int start)
    {
        for (int j = start + 1; j < instructions.Count && j <= start + 18; j++)
        {
            var ins = instructions[j];
            if (ins.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw)
                break;
            if (ins.OpCode == OpCodes.Stloc || ins.OpCode == OpCodes.Stloc_0 || ins.OpCode == OpCodes.Stloc_1 ||
                ins.OpCode == OpCodes.Stloc_2 || ins.OpCode == OpCodes.Stloc_3 || ins.OpCode == OpCodes.Stloc_S ||
                ins.OpCode == OpCodes.Stfld || ins.OpCode == OpCodes.Stsfld || ins.OpCode == OpCodes.Pop)
                break;
            if (ins.OpCode == OpCodes.Call || ins.OpCode == OpCodes.Callvirt || ins.OpCode == OpCodes.Newobj)
            {
                if (ins.Operand is not MethodReference callee) break;
                string owner = callee.DeclaringType.FullName;
                string name = callee.Name;
                string full = owner + "::" + name;
                if (IsTechnical(owner, name)) return ("TechnicalCall", full);
                if (IsConfig(owner, name)) return ("DirectConfig", full);
                if (IsUi(owner, name)) return ("DirectUI", full);
                // Avoid tagging an arbitrary earlier ldstr with a later unrelated UI call.
                break;
            }
            // Multiple ldstrs can be arguments of the same invocation, e.g. Config.Bind(section, key, ..., description).
            // No stack execution is attempted, so this is only an indication, not perfect data-flow analysis.
        }
        return ("Unknown", "");
    }

    static bool IsConfig(string owner, string name) =>
        owner.StartsWith("BepInEx.Configuration.", StringComparison.Ordinal) &&
        (name is "Bind" or ".ctor" or "set_Description" or "set_DisplayName" ||
         owner.EndsWith("ConfigFile", StringComparison.Ordinal) && name == "Bind");

    static bool IsUi(string owner, string name)
    {
        if (owner is "UnityEngine.GUI" or "UnityEngine.GUILayout" or "UnityEngine.GUIContent")
            return name is "Button" or "Box" or "Label" or "Toggle" or "TextField" or "TextArea" or "Window" or "PasswordField" or "Toolbar" or "SelectionGrid" or ".ctor";
        if (owner.StartsWith("TMPro.TMP_Text", StringComparison.Ordinal) ||
            owner is "UnityEngine.UI.Text" or "UnityEngine.UI.InputField" or "UnityEngine.UIElements.Label")
            return name is "set_text" or "SetText" or ".ctor";
        if (owner is "UnityEngine.UI.Dropdown+OptionData" or "UnityEngine.UIElements.TextElement")
            return name is ".ctor" or "set_text";
        return false;
    }

    static bool IsTechnical(string owner, string name) =>
        (name is "Find" or "FindChild" or "FindObject" or "FindObjectOfType" or "GetComponent" or
                 "GetComponentInChildren" or "GetComponentInParent" or "LoadAsset" or "LoadAssetAsync" or
                 "GetManifestResourceStream" or "GetType" or "GetProperty" or "GetMethod" or "Play" or "SetTrigger") &&
        (owner.StartsWith("UnityEngine.", StringComparison.Ordinal) || owner.StartsWith("System.Reflection.", StringComparison.Ordinal));
}
