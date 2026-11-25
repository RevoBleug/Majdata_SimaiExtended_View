using UnityEngine;

public class PlayAllPerfect : MonoBehaviour
{
    private GameObject Allperfect;
    private AudioTimeProvider timeProvider;
    private JsonDataLoader dataLoader;

    private void Start()
    {
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        dataLoader = GameObject.Find("DataLoader").GetComponent<JsonDataLoader>();
        Allperfect = GameObject.Find("CanvasAllPerfect");
        Allperfect.SetActive(false);
    }

    // Update is called once per frame
    private void Update()
    {
        // 音符是按播放进度生成的，还没生成完时不能判 AllPerfect
        if (dataLoader != null && dataLoader.HasPendingNotes) return;
        if (timeProvider.isStart && transform.childCount == 0 && Allperfect) Allperfect.SetActive(true);
    }
}