using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FurniturePlacementController))]
public class FurniturePlacementControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        FurniturePlacementController controller = (FurniturePlacementController)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "SCENE MODE FLOATING UI BUILDER:\n" +
            "Klik tombol di bawah ini untuk membuat seluruh hierarki Floating UI (Placement & Selection) langsung di Scene View.\n" +
            "Setelah dibuat, Anda dapat menggeser posisi ikon, mengganti warna, ukuran, font, dan outline secara visual di Scene Mode tanpa hilang saat Play Mode.",
            MessageType.Info
        );

        EditorGUILayout.Space(5);
        GUI.backgroundColor = new Color(0.2f, 0.75f, 0.35f, 1f); // Green
        if (GUILayout.Button("🔨 Build Floating UI in Scene", GUILayout.Height(38)))
        {
            FloatingUIEditorBuilder.BuildFloatingUI();
        }

        GUI.backgroundColor = Color.white;
        Transform root = controller.floatingUIRoot != null ? controller.floatingUIRoot.transform : null;
        if (root == null)
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas != null) root = canvas.transform.Find("FloatingUI_Root");
        }

        if (root != null)
        {
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f, 1f); // Red
            if (GUILayout.Button("🧹 Clear Built Floating UI in Scene", GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("Hapus Floating UI di Scene?", "Apakah Anda yakin ingin menghapus 'FloatingUI_Root' dari scene?", "Ya, Hapus", "Batal"))
                {
                    Undo.DestroyObjectImmediate(root.gameObject);
                    controller.floatingUIRoot = null;
                    controller.scenePlacementUI = null;
                    controller.sceneSelectionUI = null;
                    controller.sceneBtnConfirm = null;
                    controller.sceneBtnRotate = null;
                    controller.sceneBtnCancel = null;
                    controller.sceneMoveGizmoPlacement = null;
                    controller.sceneNameLabelPlacement = null;
                    controller.sceneBtnDelete = null;
                    controller.sceneBtnCable = null;
                    controller.sceneBtnRotateSelection = null;
                    controller.sceneBtnMove = null;
                    controller.sceneBtnClock = null;
                    controller.sceneMoveGizmoSelection = null;
                    controller.sceneNameLabelSelection = null;
                    EditorUtility.SetDirty(controller);
                }
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space(15);
        DrawDefaultInspector();
    }
}
