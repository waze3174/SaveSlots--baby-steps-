using System;
using BabyStepsMenuLib;
using UnityEngine;

namespace SaveSlots;

// the on-screen keyboard from BabyStepsMenuLib (the one the multiplayer mod opens for lobby passwords). it's optional,
// it only exists when that library is installed and some mod has a menu built with it. it's in its own class so
// nothing touches the library's types when it's missing
static class LibKeyboard
{
    static MenuInjectionLibrary.InjectedMenu open;
    static MenuInjectionLibrary.InputFieldInfo field;

    // false if there's no library keyboard to use
    internal static bool Open(string value, Action<string> changed, Action done)
    {
        var menu = MenuInjectionLibrary.GetKeyboardMenu();
        if (menu == null) return false;
        var info = field = new MenuInjectionLibrary.InputFieldInfo
        {
            Value = value,
            OnChanged = (UnityEngine.Events.UnityAction<string>)(v => changed(v)),
            Placeholder = "",
        };
        menu.OpenKeyboardForChat(info, done);
        if (!menu.IsKeyboardOpen) return false;
        open = menu;
        return true;
    }

    internal static bool IsOpen => open != null && open.IsKeyboardOpen;

    // a real keyboard types into it too. its text lives in our field, then it redraws. true on enter
    static System.Reflection.FieldInfo edited;
    static System.Reflection.MethodInfo refresh;

    internal static bool RealKeys(int max, Action<string> changed)
    {
        if (open == null || field == null) return false;
        string v = field.Value ?? "";
        bool any = false;
        foreach (char c in Input.inputString)
        {
            if (c == '\b') { if (v.Length > 0) { v = v.Substring(0, v.Length - 1); any = true; } }
            else if (c == '\n' || c == '\r') { field.Value = v; changed(v); return true; }
            else if (!char.IsControl(c) && v.Length < max) { v += c; any = true; }
        }
        if (!any) return false;
        field.Value = v; changed(v);
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        edited ??= typeof(MenuInjectionLibrary.InjectedMenu).GetField("_kbHasEdited", all);
        refresh ??= typeof(MenuInjectionLibrary.InjectedMenu).GetMethod("KbRefreshDisplay", all, null, Type.EmptyTypes, null);
        try { edited?.SetValue(open, true); refresh?.Invoke(open, null); } catch { }
        return false;
    }

    internal static void Close()
    {
        if (open != null && open.IsKeyboardOpen) open.CloseKeyboardOverlay();
        open = null; field = null;
    }
}
