using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SimsInventoryUI))]
public class SimsInventoryUIEditor : Editor
{
    public override void OnInspectorGUI()
    {
        SimsInventoryUI ui = (SimsInventoryUI)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "SCENE MODE UI BUILDER:\n" +
            "Klik tombol di bawah ini untuk membuat seluruh hierarki UI The Sims langsung di Scene View.\n" +
            "Setelah dibuat, Anda dapat menggeser, mengubah ukuran, anchor, dan styling objek UI secara bebas di Editor tanpa hilang saat Play Mode.",
            MessageType.Info
        );

        EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(0.2f, 0.75f, 0.35f, 1f); // Green
        if (GUILayout.Button("🔨 Build UI Hierarchy in Scene", GUILayout.Height(38)))
        {
            SimsUIEditorBuilder.BuildSimsUI();
        }

        GUI.backgroundColor = Color.white;
        Transform root = ui.transform.parent != null ? ui.transform.parent.Find("SimsInventory_Root") : null;
        if (root == null)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas != null) root = canvas.transform.Find("SimsInventory_Root");
        }

        if (root != null)
        {
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 1f); // Red
            if (GUILayout.Button("🧹 Clear Built UI in Scene", GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("Hapus UI di Scene?", "Apakah Anda yakin ingin menghapus 'SimsInventory_Root' dari scene?", "Ya, Hapus", "Batal"))
                {
                    Undo.DestroyObjectImmediate(root.gameObject);
                    ui.bottomBarRoot = null;
                    ui.closeInventoryButton = null;
                    ui.storeModalRoot = null;
                    ui.storeContentParent = null;
                    ui.cartBadgeText = null;
                    ui.storeTitleText = null;
                    ui.tabButtons.Clear();
                    EditorUtility.SetDirty(ui);
                }
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space(15);
        DrawDefaultInspector();
    }
}
