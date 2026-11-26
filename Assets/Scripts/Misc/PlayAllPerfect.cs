using UnityEngine;

public class PlayAllPerfect : MonoBehaviour
{
    private GameObject Allperfect;
    private AudioTimeProvider timeProvider;
    private JsonDataLoader dataLoader;

    private bool shown;

    private void Start()
    {
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        dataLoader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        Allperfect = GameObject.Find("CanvasAllPerfect");
        if (Allperfect != null) Allperfect.SetActive(false);
    }

    /// <summary>
    ///     每次开始播放时重置一遍。
    ///     原来这里是直接 enabled = false 把脚本关掉，但之后再也没有设回 true，
    ///     所以从第一次播放之后 AP 就永远不出了。
    /// </summary>
    public void ResetForNewChart()
    {
        if (Allperfect == null) Allperfect = GameObject.Find("CanvasAllPerfect");
        shown = false;
        if (Allperfect != null) Allperfect.SetActive(false);
    }

    // Update is called once per frame
    private void Update()
    {
        if (shown || Allperfect == null) return;
        if (timeProvider == null || !timeProvider.isStart) return;
        // 音符是按播放进度生成的，还没生成完时不能判 AllPerfect
        if (dataLoader != null && dataLoader.HasPendingNotes) return;

        // NoteDrop.LiveCount 是所有下落脚本在 Awake/OnDestroy 里自己增减的，
        // 比每帧/每 0.1 秒去数 Notes 的子物体便宜得多，
        // 也不会被 slide 判定条、hold 特效这些非音符物体影响。
        if (NoteDrop.LiveCount > 0) return;

        Show();
    }

    private void Show()
    {
        shown = true;
        Allperfect.SetActive(true);
    }
}
