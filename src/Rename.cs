using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SaveSlots;

// a name per save, shown instead of the slot letter. it's only kept in my own cfg ([SaveSlotNames]), the game's files
// are never touched. on the save screen Y (gamepad) or F2 renames the highlighted slot, enter keeps it, esc / B
// cancels, and an empty name goes back to the letter. deleting a save clears its name.
// typing goes through BabyStepsMenuLib's on-screen keyboard when it's installed, however renaming started, and a real
// keyboard types into it too. without the library you type straight in (steam's floating keyboard on a gamepad)
static class Rename
{
    const int MaxLength = 24;
    const int SaveScreen = 1;                         // Menu.currentMenuScreen while the save list is up
    const int ActionY = 16, ActionClearBinding = 19;  // rewired actions on the pad's Y (the game only uses them on the rebind screen)

    static MelonPreferences_Category cat;
    static MelonPreferences_Entry<string>[] names;

    internal static int Editing { get; private set; } = -1;
    static string buffer = "";
    static bool onLib;            // typing through BabyStepsMenuLib's keyboard
    static bool sendNav, navOff;
    static int blockUntil;        // keep the menu's input off a few frames after typing ends (that press would load the save / go back)
    static bool noLib;            // the library isn't installed

    internal static string EditText => buffer + (Time.unscaledTime % 1f < 0.5f ? "_" : " ");

    internal static void Init(string file)
    {
        cat = MelonPreferences.CreateCategory("SaveSlotNames");
        cat.SetFilePath(file);
        names = new MelonPreferences_Entry<string>[Core.MaxSlots];
        for (int k = 0; k < names.Length; k++)
            names[k] = cat.CreateEntry("Slot" + (k + 1), "", "Slot " + (k + 1), k == 0 ? "Names shown instead of the slot letter. Rename in game with Y / F2 on the save screen, or here." : null);
    }

    internal static string Get(int k)
    {
        if (names == null || k < 0 || k >= names.Length) return null;
        string s = names[k].Value?.Trim();
        return string.IsNullOrEmpty(s) ? null : s.ToUpperInvariant();
    }

    static void Set(int k, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length > MaxLength) name = name.Substring(0, MaxLength);
        if (names[k].Value == name) return;
        names[k].Value = name;
        cat.SaveToFile(false);
        Core.Inst.LoggerInstance.Msg(name.Length == 0 ? $"slot {k + 1}: name cleared" : $"slot {k + 1} named '{name}'");
    }

    internal static void Update(Il2Cpp.Menu m)
    {
        Hint(m);
        if (Editing >= 0) { Type(m); return; }
        if ((int)m.currentMenuScreen != SaveScreen) return;

        // clicking a save makes it the highlighted one, like moving there with the keys
        if (Input.GetMouseButtonDown(0)) { int c = UnderMouse(m); if (c >= 0) Pick(m, c); }

        bool key = Input.GetKeyDown(KeyCode.F2);
        var rw = m.rwPlayer;
        bool pad = !key && !Input.GetKey(KeyCode.Tab) && rw != null && (rw.GetButtonDown(ActionY) || rw.GetButtonDown(ActionClearBinding));
        if (!key && !pad) return;

        // F2 renames the save under the mouse, else the highlighted one
        int k = key ? UnderMouse(m) : -1;
        if (k >= 0) Pick(m, k); else k = Highlighted(m);
        if (k < 0) return;
        Editing = k;
        buffer = names[k].Value ?? "";
        Input.ResetInputAxes();
        onLib = OpenLib(k);
        if (pad && !onLib) SteamKeyboard(m.slotText[k]);
        // the stick / enter / esc mustn't reach the row's buttons while typing, but the library keyboard needs them for its keys
        var es = EventSystem.current;
        navOff = !onLib && es != null;
        if (navOff) { sendNav = es.sendNavigationEvents; es.sendNavigationEvents = false; }
    }

    static bool OpenLib(int k)
    {
        if (noLib) return false;
        try
        {
            return LibKeyboard.Open(buffer, v => buffer = v.Length > MaxLength ? v.Substring(0, MaxLength) : v,
                () => { if (Editing == k) { Set(k, buffer); Stop(); } });
        }
        catch (Exception e) when (e is System.IO.FileNotFoundException || e is TypeLoadException || e is MissingMethodException)
        {
            noLib = true;   // BabyStepsMenuLib not installed (or a version without the keyboard)
            return false;
        }
    }

    // which slot row the game's menu list has highlighted
    static int Highlighted(Il2Cpp.Menu m)
    {
        var list = Il2Cpp.MenuItemList.active;
        if (list == null || list.items == null || list.selected < 0 || list.selected >= list.items.Length) return -1;
        var go = list.items[list.selected];
        var st = m.slotText;
        for (int k = 0; k < st.Length; k++)
            if (st[k] != null && go != null && (st[k].gameObject == go || st[k].transform.IsChildOf(go.transform))) return k;
        return -1;
    }

    // the menu list's row holding slot k's text
    static int RowOf(Il2Cpp.MenuItemList list, TextMeshProUGUI t)
    {
        if (list == null || list.items == null || t == null) return -1;
        for (int r = 0; r < list.items.Length; r++)
        {
            var go = list.items[r];
            if (go != null && (t.gameObject == go || t.transform.IsChildOf(go.transform))) return r;
        }
        return -1;
    }

    // which slot row the mouse is over (the whole row, not just its text)
    static int UnderMouse(Il2Cpp.Menu m)
    {
        var list = Il2Cpp.MenuItemList.active;
        var st = m.slotText;
        if (list == null || st == null) return -1;
        for (int k = 0; k < st.Length; k++)
        {
            int r = RowOf(list, st[k]);
            if (r < 0) continue;
            var rt = list.items[r].GetComponent<RectTransform>() ?? st[k].rectTransform;
            var canvas = st[k].canvas;
            var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, (Vector2)Input.mousePosition, cam)) return k;
        }
        return -1;
    }

    static void Pick(Il2Cpp.Menu m, int k)
    {
        var list = Il2Cpp.MenuItemList.active;
        int r = RowOf(list, m.slotText[k]);
        if (r < 0 || list.selected == r) return;
        list.selected = r;
        list.Select();
    }

    static void Type(Il2Cpp.Menu m)
    {
        // left the save screen some other way (mouse click on back...)
        if ((int)m.currentMenuScreen != SaveScreen) { Stop(); return; }
        if (onLib)
        {
            if (!LibKeyboard.IsOpen) { Stop(); return; }   // closed without done is a cancel (done already saved and stopped)
            if (LibKeyboard.RealKeys(MaxLength, v => buffer = v)) { Set(Editing, buffer); Stop(); }
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1)) { Stop(); return; }
        foreach (char c in Input.inputString)
        {
            if (c == '\b') { if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1); }
            else if (c == '\n' || c == '\r') { Set(Editing, buffer); Stop(); return; }
            else if (!char.IsControl(c) && buffer.Length < MaxLength) buffer += c;
        }
    }

    internal static void Stop()
    {
        if (Editing < 0) return;
        Editing = -1;
        blockUntil = Time.frameCount + 10;
        if (onLib) { try { LibKeyboard.Close(); } catch { } onLib = false; }
        var es = EventSystem.current;
        if (navOff && es != null) es.sendNavigationEvents = sendNav;
        navOff = false;
    }

    static bool MenuInputOff => Editing >= 0 || Time.frameCount <= blockUntil;

    // while typing, the menu mustn't see enter / A (it would load the save), esc / B (it would leave) or the stick
    [HarmonyPatch(typeof(Il2Cpp.Menu), "PausedInput")]
    static class BlockMenuInput { static bool Prefix() => !MenuInputOff; }

    [HarmonyPatch(typeof(Il2Cpp.MenuItemList), "Update")]
    static class BlockListInput { static bool Prefix() => !MenuInputOff; }

    // nor should the minimap mod (its keys and pad buttons open the map or switch views). it reads its keys (M for the
    // map...) right in its OnUpdate, so its whole update waits while typing
    internal static void MuteMinimap(HarmonyLib.Harmony h)
    {
        var t = AccessTools.TypeByName("BabyStepsMinimap.Core");
        if (t == null) return;
        foreach (var name in new[] { "OnUpdate", "HandleGameplayInput", "HandleHelpInput", "HandleFullMapInput", "HandleJourneyInput", "HandleCalibrationInput", "HandleStickClicks" })
        {
            var m = AccessTools.Method(t, name);
            if (m != null) h.Patch(m, prefix: new HarmonyMethod(typeof(Rename), nameof(MinimapPrefix)));
        }
    }

    static bool MinimapPrefix() => !MenuInputOff;

    // a deleted save loses its name. the first press only turns DELETE into "you sure?", the second one deletes
    [HarmonyPatch(typeof(Il2Cpp.Menu), nameof(Il2Cpp.Menu.DeleteSaveButton))]
    static class ClearOnDelete
    {
        static void Prefix(Il2Cpp.Menu __instance, int i, out bool __state)
        {
            var dt = __instance.deleteText;
            __state = dt != null && i >= 0 && i < dt.Length && dt[i] != null && dt[i].text == Il2Cpp.Menu.GetTranslated(Il2Cpp.LocTerm.MSID106_YOU_SURE);
        }

        static void Postfix(int i, bool __state)
        {
            if (__state && Get(i) != null) Set(i, "");
        }
    }

    // ---- "Y / F2: NAME A SAVE" beside Back on the save screen ----

    static TextMeshProUGUI hint;
    static Il2Cpp.Menu hintFor;

    static void Hint(Il2Cpp.Menu m)
    {
        if (hintFor != m || hint == null) { hintFor = m; hint = MakeHint(m); }
        if (hint == null) return;
        string want = Editing >= 0 ? (onLib ? "TYPING A NAME" : "ENTER: KEEP   ESC: CANCEL") : "Y / F2: NAME A SAVE";
        if (hint.text != want) hint.text = want;
        // beside Back, lined up with the DELETE column. the list gets fitted after this is made, so it follows every frame
        var dt = m.deleteText;
        var items = hint.transform.parent.GetComponent<Il2Cpp.MenuItemList>()?.items;
        var back = items == null || items.Length == 0 || items[items.Length - 1] == null ? null : items[items.Length - 1].transform;   // the game's Back
        if (back == null || dt == null || dt[dt.Length - 1] == null) return;
        var del = dt[dt.Length - 1].rectTransform;
        var cs = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
        del.GetWorldCorners(cs);
        hint.transform.position = new Vector3(cs[0].x, back.position.y, back.position.z);
        hint.transform.localScale = Vector3.one;
    }

    static TextMeshProUGUI MakeHint(Il2Cpp.Menu m)
    {
        var st = m.slotText;
        if (st == null || st.Length < 2 || st[0] == null || st[1] == null) return null;
        var list = Core.Common(st[0].transform, st[1].transform);
        if (list == null) return null;
        var go = new GameObject("SaveSlots hint");
        go.transform.SetParent(list, false);
        go.AddComponent<LayoutElement>().ignoreLayout = true;   // not part of the list's layout or its gamepad list
        var t = go.AddComponent<TextMeshProUGUI>();
        t.font = st[0].font;
        t.fontSharedMaterial = st[0].fontSharedMaterial;
        t.fontSize = st[0].fontSize * 0.55f;
        t.color = new Color(st[0].color.r, st[0].color.g, st[0].color.b, st[0].color.a * 0.75f);
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(600f, st[0].rectTransform.rect.height);
        return t;
    }

    // ---- steam's floating keyboard (deck / big picture), it types into the game as key presses ----

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr SteamAPI_SteamUtils_v010();

    [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    static extern bool SteamAPI_ISteamUtils_ShowFloatingGamepadTextInput(IntPtr self, int mode, int x, int y, int w, int h);

    static void SteamKeyboard(TextMeshProUGUI row)
    {
        try
        {
            var utils = SteamAPI_SteamUtils_v010();
            if (utils == IntPtr.Zero) return;
            // the text field's screen rect (steam wants top-left origin) so the keyboard doesn't cover it
            var cs = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            row.rectTransform.GetWorldCorners(cs);
            var canvas = row.canvas;
            var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var a = RectTransformUtility.WorldToScreenPoint(cam, cs[0]);
            var b = RectTransformUtility.WorldToScreenPoint(cam, cs[2]);
            bool ok = SteamAPI_ISteamUtils_ShowFloatingGamepadTextInput(utils, 0, (int)a.x, Screen.height - (int)b.y, (int)(b.x - a.x), (int)(b.y - a.y));
            if (!ok) Core.Inst.LoggerInstance.Msg("steam keyboard didn't open (type on a keyboard, or open steam's keyboard yourself)");
        }
        catch (Exception e) { Core.Inst.LoggerInstance.Warning("steam keyboard: " + e.Message); }
    }
}
