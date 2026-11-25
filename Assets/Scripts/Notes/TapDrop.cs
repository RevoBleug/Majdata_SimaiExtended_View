using System.Diagnostics;
using System.Net.NetworkInformation;
using UnityEngine;

public class TapDrop : NoteDrop
{
    // Start is called before the first frame update
    // public float time;
    public int startPosition = 1;
    public float speed = 1;

    public bool isEach;
    public bool isBreak;
    public bool isEX;
    public bool isMine;
    public bool isFakeStarRotate;
    public bool canSVAffect;

    public Sprite normalSpr;
    public Sprite eachSpr;
    public Sprite breakSpr;
    public Sprite exSpr;
    public Sprite mineSpr;

    public Sprite eachLine;
    public Sprite breakLine;
    public Sprite mineLine;

    public RuntimeAnimatorController BreakShine;

    public GameObject tapLine;

    public Color exEffectTap;
    public Color exEffectEach;
    public Color exEffectBreak;
    private Animator animator;

    private bool breakAnimStart;
    private SpriteRenderer exSpriteRender;
    private SpriteRenderer lineSpriteRender;

    private ObjectCounter ObjectCounter;

    private SpriteRenderer spriteRenderer;

    private AudioTimeProvider timeProvider;

    private int GetSortOrder()
    {
        return noteSortOrder;
    }

    private void Start()
    {
        var notes = GameObject.Find("Notes").transform;
        tapLine = Instantiate(tapLine, notes);
        tapLine.SetActive(false);
        lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        ObjectCounter = GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>();

        spriteRenderer.sortingOrder += noteSortOrder;
        exSpriteRender.sortingOrder += noteSortOrder;

        spriteRenderer.sprite = normalSpr;
        exSpriteRender.sprite = exSpr;

        if (isEX && !isMine) exSpriteRender.color = exEffectTap;
        if (isEach && !isMine)
        {
            spriteRenderer.sprite = eachSpr;
            lineSpriteRender.sprite = eachLine;
            if (isEX) exSpriteRender.color = exEffectEach;
        }
        if (isMine)
        {
            spriteRenderer.sprite = mineSpr;
            lineSpriteRender.sprite = mineLine;
        }
        if (isBreak && !isMine)
        {
            spriteRenderer.sprite = breakSpr;
            lineSpriteRender.sprite = breakLine;
            if (isEX) exSpriteRender.color = exEffectBreak;
            var anim = gameObject.AddComponent<Animator>(); // break tap闪烁
            anim.runtimeAnimatorController = BreakShine;
            anim.enabled = false;
            animator = anim;
        }

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
    }

    // Update is called once per frame
    private void Update()
    {
        var timing = timeProvider.GetTiming(time, svGroup);
        var realtime = (float)(timeProvider.AudioTime - time);
        var distance = timing * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        var realDistance = realtime * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        if(!canSVAffect)
        {
            timing = realtime;
            distance = realDistance;
        }
        var destScale = distance * 0.4f + 0.51f;
        if (destScale < 0f)
        {
            destScale = 0f;
            tapLine.SetActive(false);
            spriteRenderer.forceRenderingOff = true;
            return;
        }

        spriteRenderer.forceRenderingOff = false;
        if (isEX) exSpriteRender.forceRenderingOff = false;
        if (isBreak && !breakAnimStart)
        {
            breakAnimStart = true;
            animator.enabled = true;
            animator.Play("BreakShine", -1, 0.5f);
        }

        if (realtime > 0)
        {
            if(!isMine) GameObject.Find("NoteEffects").GetComponent<NoteEffectManager>().PlayEffect(startPosition, isBreak);
            if (isMine) ObjectCounter.mineCount++;
            else if (isBreak) ObjectCounter.breakCount++;
            else ObjectCounter.tapCount++;

            Destroy(tapLine);
            Destroy(gameObject);
        }

        if (isFakeStarRotate)
            transform.Rotate(0f, 0f, 400f * Time.deltaTime);
        else
            transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));
        tapLine.transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));

        if (distance < 1.225f)
        {
            transform.localScale = new Vector3(destScale, destScale);

            distance = 1.225f;
            var pos = getPositionFromDistance(distance);
            transform.position = pos;
        }
        else
        {
            var pos = getPositionFromDistance(distance);
            transform.position = pos;
            transform.localScale = new Vector3(1f, 1f);
        }

        var lineScale = Mathf.Abs(distance / 4.8f);
        tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
        // 线的显隐必须每帧统一判断：
        // 负 SV 回弹时 distance 可能一帧就从判定圈内侧跨到外面，跳过上面的 <1.225 分支，
        // 而 SetActive(true) 只写在那分支里，line 就会一直停在上一次的关闭状态（表现为时有时无）。
        // 注意：线是从判定圈中心连到音符的，远处也要显示（靠 localScale 拉长），
        // 所以这里不能加 distance 条件，只按"音符是否退到太内侧"判断。
        tapLine.SetActive(destScale > 0.3f);
        //lineSpriteRender.color = new Color(1f, 1f, 1f, lineScale);
    }

    private Vector3 getPositionFromDistance(float distance)
    {
        return new Vector3(
            distance * Mathf.Cos((startPosition * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((startPosition * -2f + 5f) * 0.125f * Mathf.PI));
    }
}