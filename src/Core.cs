using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(SaveSlots.Core), "SaveSlots", "1.0.2", "waze3174")]
[assembly: MelonGame]
[assembly: MelonOptionalDependencies("BabyStepsMenuLib")]

namespace SaveSlots;

// more than the game's 3 save slots. the game's save code already works with any slot number (save{i}.sav ...),
// it's only the title menu that has 3 rows, a 3-long cachedSaveDays and 3 slot names (ALPHA / BETA / SIGMA)
public class Core : MelonMod
{
    internal static Core Inst;
    internal static MelonPreferences_Entry<int> SlotCount;

    // names for slots past the game's three
    // 12 slots at most, past that the list has to shrink too much to fit
    static readonly string[] Names = { "GAMMA", "DELTA", "EPSILON", "ZETA", "ETA", "THETA", "IOTA", "KAPPA", "LAMBDA" };
    internal static int MaxSlots => 3 + Names.Length;

    public override void OnInitializeMelon()
    {
        Inst = this;
        var cat = MelonPreferences.CreateCategory("SaveSlots");
        string dir = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "SaveSlots");
        Directory.CreateDirectory(dir);
        cat.SetFilePath(Path.Combine(dir, "SaveSlots.cfg"));
        SlotCount = cat.CreateEntry("SlotCount", 6, "Save slots", $"How many save slots the title menu shows (3 to {MaxSlots}). Saves in slots you hide are kept.");
        Rename.Init(Path.Combine(dir, "SaveSlots.cfg"));
        LoggerInstance.Msg($"{Count} save slots");
    }

    public override void OnLateInitializeMelon()
    {
        try { Rename.MuteMinimap(HarmonyInstance); } catch (Exception e) { LoggerInstance.Warning("minimap: " + e.Message); }
    }

    internal static int Count => Math.Clamp(SlotCount.Value, 3, MaxSlots);
    internal static string SlotName(int k) => k < 3 ? null : Names[k - 3];

    // ---- menu rows ----

    static Il2Cpp.Menu built;        // the Menu whose rows we've already copied (a new one comes with every scene restart)

    [HarmonyPatch(typeof(Il2Cpp.Menu), "Update")]
    static class MenuUpdate
    {
        static void Prefix(Il2Cpp.Menu __instance)
        {
            try { Grow(__instance); }
            catch (Exception e) { Once("grow", e); built = __instance; }
        }

        // past row 3 the game labels every row with slot 1's name, every frame, so ours go on after it
        static void Postfix(Il2Cpp.Menu __instance)
        {
            try { Relabel(__instance); } catch (Exception e) { Once("relabel", e); }
            try { TryFit(); } catch (Exception e) { Once("fit", e); toFit = null; }
            try { Rename.Update(__instance); } catch (Exception e) { Once("rename", e); Rename.Stop(); }
        }
    }

    // these run every frame: an error is logged once, not 60 times a second
    static readonly HashSet<string> told = new();
    static void Once(string what, Exception e)
    {
        if (told.Add(what)) Inst.LoggerInstance.Error(what + ": " + e);
    }

    static void Grow(Il2Cpp.Menu m)
    {
        var st = m.slotText; var dt = m.deleteText;
        if (st == null || dt == null || st.Length < 3 || dt.Length < 3) return;
        int n = Count;

        // LoadSave makes a fresh int[3] after every scene load, and Update reads it by row
        var days = m.cachedSaveDays;
        if (days != null && days.Length < n)
        {
            var nd = new Il2CppStructArray<int>(n);
            for (int k = 0; k < n; k++) nd[k] = k < days.Length ? days[k] : ReadSaveDay(k);
            m.cachedSaveDays = nd;
        }

        if (built == m || st.Length >= n) return;
        built = m;

        var slotRows = Rows(st);
        var delRows = Rows(dt);
        if (slotRows == null) { Inst.LoggerInstance.Warning("couldn't find the slot rows"); return; }
        bool sameRow = delRows == null || delRows[0] == slotRows[0];

        var list = slotRows[0].parent.TryCast<RectTransform>();

        var nst = new Il2CppReferenceArray<TextMeshProUGUI>(n);
        var ndt = new Il2CppReferenceArray<TextMeshProUGUI>(n);
        for (int k = 0; k < 3; k++) { nst[k] = st[k]; ndt[k] = dt[k]; }

        var newSlotRows = new List<Transform>(slotRows);
        var newDelRows = new List<Transform>(sameRow ? slotRows : delRows);
        for (int k = 3; k < n; k++)
        {
            var row = Copy(slotRows, k);
            nst[k] = Find(row, st[2]);
            newSlotRows.Add(row);
            Transform drow = row;
            if (!sameRow) { drow = Copy(delRows, k); newDelRows.Add(drow); }
            ndt[k] = Find(drow, dt[2]);
            Wire(m, row, drow, sameRow, ndt[k], k);
        }
        m.slotText = nst;
        m.deleteText = ndt;
        AddToItemList(newSlotRows);
        // name the copies "Gamma"... like the game's "Sigma" (after AddToItemList, it finds them by the original's name)
        for (int k = 3; k < n; k++)
            if (nst[k] != null) nst[k].gameObject.name = SlotName(k)[0] + SlotName(k).Substring(1).ToLowerInvariant();
        var il = slotRows[0].parent.GetComponent<Il2Cpp.MenuItemList>();
        if (il != null && il.items != null)
        {
            var names = new List<string>();
            foreach (var g in il.items) names.Add(g == null ? "-" : g.name);
            DebugLog.Write("gamepad list: " + string.Join(", ", names));
        }
        if (list != null) { toFit = list; fitRows = slotRows; fitExtra = n - 3; }
        DebugLog.Write($"added slots 4-{n} ({(sameRow ? "delete button in the row" : "separate delete column")})");
    }

    // ---- fitting the longer list on screen ----
    // the list grows down from where the game put it, past the bottom of the screen (Back too). i keep its bottom
    // where the game had it, let it grow up into the space under the title, and shrink it if that's not enough

    static RectTransform toFit; static Transform[] fitRows; static int fitExtra;

    // the save screen isn't laid out until it first shows, so it's fitted then. where the game had the bottom is the
    // bottom now, raised by the rows i added
    static void TryFit()
    {
        if (toFit == null) return;
        if (fitRows[0] == null) { toFit = null; return; }
        float step = ScreenY(fitRows[0].TryCast<RectTransform>(), 1) - ScreenY(fitRows[1].TryCast<RectTransform>(), 1);
        if (step <= 1f) return;
        var list = toFit; toFit = null;
        Fit(list, fitRows, ListBottom(list) + fitExtra * step);
    }

    const float TopLimit = 0.78f;   // highest the first row may reach, as a fraction of the screen height (the title sits above)

    static void Fit(RectTransform list, Transform[] rows, float bottom0)
    {
        float top = ScreenY(rows[0].TryCast<RectTransform>(), 1);
        float need = top - ListBottom(list);
        float avail = Screen.height * TopLimit - bottom0;
        if (need <= 0 || avail <= 0) { Inst.LoggerInstance.Warning($"save list not fitted: top {top:F0}, need {need:F0}, room {avail:F0} px"); return; }
        float s = Mathf.Min(1f, avail / need);
        if (s < 1f) list.localScale = list.localScale * s;

        // screen pixels per unit of the list's parent, from two rows' spacing
        Rebuild(list);
        float px = ScreenY(rows[0].TryCast<RectTransform>(), 1) - ScreenY(rows[1].TryCast<RectTransform>(), 1);
        float units = (rows[0].localPosition.y - rows[1].localPosition.y) * list.localScale.y;
        if (px <= 0 || units <= 0) { Inst.LoggerInstance.Warning($"save list not moved: {px:F1} px over {units:F1} units"); return; }
        var p = list.anchoredPosition;
        p.y += (bottom0 - ListBottom(list)) / (px / units);
        list.anchoredPosition = p;
        Rebuild(list);
        DebugLog.Write($"save list fitted: scale {s:F2}, rows {ScreenY(rows[0].TryCast<RectTransform>(), 1):F0} to {ListBottom(list):F0} px of {Screen.height}");
    }

    static void Rebuild(RectTransform list)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(list);
    }

    // lowest bottom edge of the list's active children, in screen pixels from the bottom
    static float ListBottom(RectTransform list)
    {
        Rebuild(list);
        float b = float.MaxValue;
        for (int i = 0; i < list.childCount; i++)
        {
            var c = list.GetChild(i).TryCast<RectTransform>();
            if (c != null && c.gameObject.activeSelf) b = Mathf.Min(b, ScreenY(c, 0));
        }
        return b == float.MaxValue ? 0 : b;
    }

    // corner 0 = bottom-left, 1 = top-left
    static float ScreenY(RectTransform rt, int corner)
    {
        var cs = new Il2CppStructArray<Vector3>(4);
        rt.GetWorldCorners(cs);
        var canvas = rt.GetComponentInParent<Canvas>();
        var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return RectTransformUtility.WorldToScreenPoint(cam, cs[corner]).y;
    }

    // the row of each text: the child of their common parent that holds it
    static Transform[] Rows(Il2CppReferenceArray<TextMeshProUGUI> texts)
    {
        var parent = Common(texts[0].transform, texts[1].transform);
        if (parent == null) return null;
        var rows = new Transform[3];
        for (int k = 0; k < 3; k++)
        {
            var t = texts[k].transform;
            while (t != null && t.parent != parent) t = t.parent;
            if (t == null) return null;
            rows[k] = t;
        }
        return rows;
    }

    internal static Transform Common(Transform a, Transform b)
    {
        for (var x = a.parent; x != null; x = x.parent)
            for (var y = b.parent; y != null; y = y.parent)
                if (x == y) return x;
        return null;
    }

    // a copy of the last row, one row further down (unless a layout group places it)
    static Transform Copy(Transform[] rows, int k)
    {
        var src = rows[2];
        var copy = UnityEngine.Object.Instantiate(src.gameObject, src.parent).transform;
        copy.name = src.name + " (slot " + (k + 1) + ")";
        copy.SetSiblingIndex(src.GetSiblingIndex() + (k - 2));
        if (src.parent.GetComponent<LayoutGroup>() == null)
        {
            var step = src.localPosition - rows[1].localPosition;
            copy.localPosition = src.localPosition + step * (k - 2);
        }
        return copy;
    }

    // the copy of `orig` inside `row`, found by its path under the original row
    static TextMeshProUGUI Find(Transform row, TextMeshProUGUI orig)
    {
        foreach (var t in row.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (t.name == orig.name) return t;
        return null;
    }

    // copies keep the original's onClick (SetSaveSlot(2) / DeleteSaveButton(2)), so they get pointed at slot k
    static void Wire(Il2Cpp.Menu m, Transform row, Transform drow, bool sameRow, TextMeshProUGUI del, int k)
    {
        foreach (var r in sameRow ? new[] { row } : new[] { row, drow })
            foreach (var b in r.GetComponentsInChildren<Button>(true))
            {
                bool isDel = del != null && (del.transform.IsChildOf(b.transform) || (!sameRow && r == drow));
                for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++) b.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
                b.onClick.RemoveAllListeners();
                int slot = k;
                if (isDel) b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => m.DeleteSaveButton(slot))));
                else b.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => m.SetSaveSlot(slot))));
            }
    }

    // gamepad up / down on the save screen goes through the game's MenuItemList (unity's navigation is off), so each
    // copy goes in right after the last original row. left / right (slot <-> delete) is a HorizontalButtonSwitcher
    // inside the row, and the copy's already points at its own buttons
    static void AddToItemList(List<Transform> rows)
    {
        var list = rows[0].parent.GetComponent<Il2Cpp.MenuItemList>();
        if (list == null || list.items == null) { Inst.LoggerInstance.Warning("no MenuItemList on the save screen: gamepad won't reach the new slots"); return; }
        var items = new List<GameObject>();
        foreach (var go in list.items) items.Add(go);

        int at = -1; string rel = null;
        for (int i = 0; i < items.Count; i++)
            if (items[i] != null && (items[i].transform == rows[2] || items[i].transform.IsChildOf(rows[2]))) { at = i; rel = RelPath(rows[2], items[i].transform); }
        if (at < 0) { Inst.LoggerInstance.Warning("the last slot isn't in the save screen's MenuItemList: gamepad won't reach the new slots"); return; }

        for (int r = rows.Count - 1; r >= 3; r--)
        {
            var t = rel.Length == 0 ? rows[r] : rows[r].Find(rel);
            if (t != null) items.Insert(at + 1, t.gameObject);
        }
        var arr = new Il2CppReferenceArray<GameObject>(items.Count);
        for (int i = 0; i < items.Count; i++) arr[i] = items[i];
        list.items = arr;
    }

    static string RelPath(Transform root, Transform t) => t == root ? "" : (t.parent == root ? t.name : RelPath(root, t.parent) + "/" + t.name);

    // the game writes "<slot name> (<place>)" on every row each frame. i swap the slot name for the name you gave that
    // save, or for my greek letter past slot 3 (the game puts ALPHA there)
    static readonly Il2Cpp.LocTerm[] GameNames = { Il2Cpp.LocTerm.MSID06_ALPHA, Il2Cpp.LocTerm.MSID11_BETA, Il2Cpp.LocTerm.MSID88_SIGMA };

    static void Relabel(Il2Cpp.Menu m)
    {
        var st = m.slotText;
        if (st == null) return;
        for (int k = 0; k < st.Length; k++)
        {
            var t = st[k];
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            if (Rename.Editing == k) { string e = Rename.EditText; if (t.text != e) t.text = e; continue; }
            string label = Rename.Get(k) ?? SlotName(k);
            if (label == null) continue;
            string prefix = Il2Cpp.Menu.GetTranslated(GameNames[k < 3 ? k : 0]);
            string s = t.text;
            if (s != null && prefix != null && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                t.text = label + s.Substring(prefix.Length);
        }
    }

    // ---- reading a save's day without loading it ----

    static string saveDir;

    // the save is a MessagePack array, and key 1 is lastCampfire, which is what the menu shows (dayPlaceNames[lastCampfire])
    static int ReadSaveDay(int k)
    {
        try
        {
            string dir = SaveDir();
            if (dir == null) return -1;
            foreach (var name in new[] { $"save{k}.sav", $"saveBackup{k}.sav" })
            {
                string p = Path.Combine(dir, name);
                if (!File.Exists(p)) continue;
                var r = new MsgPack(File.ReadAllBytes(p));
                if (r.ArrayHeader() < 2) continue;
                r.Skip();
                return (int)r.Int();
            }
        }
        catch (Exception e) { Inst.LoggerInstance.Warning($"slot {k + 1}: couldn't read its save ({e.Message})"); }
        return -1;
    }

    // the folder holding save0.sav etc., somewhere under persistentDataPath (<steamid>/save on steam)
    static string SaveDir()
    {
        if (saveDir != null) return saveDir;
        string root = Application.persistentDataPath;
        foreach (var f in Directory.GetFiles(root, "save*.sav", SearchOption.AllDirectories))
        {
            saveDir = Path.GetDirectoryName(f);
            DebugLog.Write("saves in " + saveDir);
            break;
        }
        return saveDir;
    }

    class MsgPack
    {
        readonly byte[] b; int p;
        public MsgPack(byte[] bytes) { b = bytes; }
        byte U8() => b[p++];
        int U16() { int v = b[p] << 8 | b[p + 1]; p += 2; return v; }
        long U32() { long v = (long)b[p] << 24 | (long)b[p + 1] << 16 | (long)b[p + 2] << 8 | b[p + 3]; p += 4; return v; }

        public int ArrayHeader()
        {
            byte t = U8();
            if ((t & 0xF0) == 0x90) return t & 0x0F;
            if (t == 0xDC) return U16();
            if (t == 0xDD) return (int)U32();
            throw new InvalidDataException("not an array");
        }

        public long Int()
        {
            byte t = U8();
            if (t <= 0x7F) return t;
            if (t >= 0xE0) return (sbyte)t;
            switch (t)
            {
                case 0xCC: return U8();
                case 0xCD: return U16();
                case 0xCE: return U32();
                case 0xD0: return (sbyte)U8();
                case 0xD1: return (short)U16();
                case 0xD2: return (int)U32();
                default: throw new InvalidDataException($"not an int (0x{t:X2})");
            }
        }

        public void Skip()
        {
            byte t = U8();
            if (t <= 0x7F || t >= 0xE0 || t == 0xC0 || t == 0xC2 || t == 0xC3) return;
            if ((t & 0xE0) == 0xA0) { p += t & 0x1F; return; }
            if ((t & 0xF0) == 0x90) { for (int i = t & 0x0F; i > 0; i--) Skip(); return; }
            if ((t & 0xF0) == 0x80) { for (int i = (t & 0x0F) * 2; i > 0; i--) Skip(); return; }
            switch (t)
            {
                case 0xCC: case 0xD0: p += 1; return;
                case 0xCD: case 0xD1: p += 2; return;
                case 0xCE: case 0xD2: case 0xCA: p += 4; return;
                case 0xCF: case 0xD3: case 0xCB: p += 8; return;
                // the length first, then the jump (p += U8() would read p before U8 moves it: a byte short)
                case 0xD9: case 0xC4: { int n = U8(); p += n; return; }
                case 0xDA: case 0xC5: { int n = U16(); p += n; return; }
                case 0xDB: case 0xC6: { int n = (int)U32(); p += n; return; }
                case 0xDC: for (int i = U16(); i > 0; i--) Skip(); return;
                case 0xDD: for (long i = U32(); i > 0; i--) Skip(); return;
                case 0xDE: for (int i = U16() * 2; i > 0; i--) Skip(); return;
                case 0xDF: for (long i = U32() * 2; i > 0; i--) Skip(); return;
                case 0xD4: p += 2; return;
                case 0xD5: p += 3; return;
                case 0xD6: p += 5; return;
                case 0xD7: p += 9; return;
                case 0xD8: p += 17; return;
                case 0xC7: { int n = U8(); p += n + 1; return; }
                case 0xC8: { int n = U16(); p += n + 1; return; }
                case 0xC9: { int n = (int)U32(); p += n + 1; return; }
                default: throw new InvalidDataException($"bad type 0x{t:X2}");
            }
        }
    }
}
