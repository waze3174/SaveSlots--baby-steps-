using System;
using BabyStepsMenuLib;
using UnityEngine;

namespace SaveSlots;

// the on-screen keyboard from BabyStepsMenuLib (the one the multiplayer mod opens for lobby passwords).
// optional: it only exists when that library is installed and some mod has a menu built with it.
// kept in its own class so nothing touches the library's types when it's missing
static class LibKeyboard
{
    static MenuInjectionLibrary.InjectedMenu open;

    // false if there's no library keyboard to use
    internal static bool Open(string value, Action<string> changed, Action done)
    {
        var menu = MenuInjectionLibrary.GetKeyboardMenu();
        if (menu == null) return false;
        var info = new MenuInjectionLibrary.InputFieldInfo
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

    internal static void Close()
    {
        if (open != null && open.IsKeyboardOpen) open.CloseKeyboardOverlay();
        open = null;
    }
}
