// Значения обеих полос и подписи — то, что видит человек.
var wins = UnityEngine.Resources.FindObjectsOfTypeAll(typeof(Woodberry.Editor.ArtStudio.ArtStudioWindow));
var window = (UnityEditor.EditorWindow)wins[0];
UnityEngine.UIElements.VisualElement root = window.rootVisualElement;

UnityEngine.UIElements.ProgressBar overall =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.ProgressBar>(root, "generationBar").First();
UnityEngine.UIElements.ProgressBar frame =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.ProgressBar>(root, "frameBar").First();
UnityEngine.UIElements.Label oc =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root, "overallCaption").First();
UnityEngine.UIElements.Label fc =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root, "frameCaption").First();
UnityEngine.UIElements.Label phase =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root, "generationPhase").First();
UnityEngine.UIElements.Label eta =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root, "generationEta").First();
UnityEngine.UIElements.Label title =
    UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root, "generationTitle").First();

return "общая=" + overall.value.ToString("0") + " кадр=" + frame.value.ToString("0")
    + " | \"" + oc.text + "\" | \"" + fc.text + "\""
    + " | фаза=\"" + phase.text + "\" | \"" + eta.text + "\" | \"" + title.text + "\"";
