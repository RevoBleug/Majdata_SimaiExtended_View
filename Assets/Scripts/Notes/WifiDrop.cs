using System;
using System.Collections.Generic;
using UnityEngine;

public class WifiDrop : NoteLongDrop
{
    // Start is called before the first frame update
    public GameObject star_slidePrefab;

    public Sprite[] normalSlide = new Sprite[11];
    public Sprite[] eachSlide = new Sprite[11];
    public Sprite[] breakSlide = new Sprite[11];
    public Sprite[] mineSlide = new Sprite[11];
    public Sprite normalStar;
    public Sprite eachStar;
    public Sprite breakStar;
    public Sprite mineStar;

    public RuntimeAnimatorController slideShine;

    public bool isJustR;

    // public float time;
    public float timeStart;

    // public float LastFor = 1f;
    public float speed;
    public bool isEach;
    public bool isBreak;
    public bool isMine;
    public bool isGroupPart;
    public bool isGroupPartEnd;
    public bool canSVAffect;
    //public double lastSlideTime;

    public int startPosition = 1;

    public int sortIndex;

    public List<int> areaStep = new List<int>();
    public bool smoothSlideAnime = false;

    private readonly List<Animator> animators = new();
    private readonly List<SpriteRenderer> sbRender = new();

    private readonly List<GameObject> slideBars = new();
    private readonly Vector3[] SlidePositionEnd = new Vector3[3];

    private readonly SpriteRenderer[] spriteRenderer_star = new SpriteRenderer[3];
    private readonly GameObject[] star_slide = new GameObject[3];
    private GameObject slideOK;

    private Vector3 SlidePositionStart;

    private bool startShining;
    private bool slidingStarted; // 是否已经开始移动（防止 SV 回退时星星跳回起点）

    /// <summary>判定条的存活时间（秒）。原来亮完没人销毁，会永久堆在 Notes 下面。</summary>
    private const float JudgementBarLifetime = 1.5f;

    private bool slideOkScheduled;

    private AudioTimeProvider timeProvider;

    private void Start()
    {
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        var notes = GameObject.Find("Notes").transform;
        for (var i = 0; i < star_slide.Length; i++)
        {
            star_slide[i] = Instantiate(star_slidePrefab, notes);
            spriteRenderer_star[i] = star_slide[i].GetComponent<SpriteRenderer>();
            if (isMine) spriteRenderer_star[i].sprite = mineStar;
            else if (isEach) spriteRenderer_star[i].sprite = eachStar;
            else if (isBreak) spriteRenderer_star[i].sprite = breakStar;
            else spriteRenderer_star[i].sprite = normalStar;
            star_slide[i].transform.rotation = Quaternion.Euler(0, 0, -22.5f * (8 + i + 2 * (startPosition - 1)));
            SlidePositionEnd[i] = getPositionFromDistance(4.8f, i + 3 + startPosition);
            star_slide[i].SetActive(false);
        }

        transform.rotation = Quaternion.Euler(0f, 0f, -45f * (startPosition - 1));
        slideBars.Clear();
        for (var i = 0; i < transform.childCount - 1; i++) slideBars.Add(transform.GetChild(i).gameObject);
        slideOK = transform.GetChild(transform.childCount - 1).gameObject; //slideok is the last one
        if (isJustR)
        {
            slideOK.GetComponent<LoadJustSprite>().setR();
        }
        else
        {
            slideOK.GetComponent<LoadJustSprite>().setL();
            slideOK.transform.Rotate(new Vector3(0f, 0f, 180f));
        }

        slideOK.SetActive(false);
        slideOK.transform.SetParent(transform.parent);
        SlidePositionStart = getPositionFromDistance(4.8f);

        for (var i = 0; i < slideBars.Count; i++)
        {
            var sr = slideBars[i].GetComponent<SpriteRenderer>();

            if (isMine)
            {
                sr.sprite = mineSlide[i];
            }
            else if (isBreak)
            {
                sr.sprite = breakSlide[i];
                var anim = slideBars[i].AddComponent<Animator>();
                anim.runtimeAnimatorController = slideShine;
                anim.enabled = false;
                animators.Add(anim);
            }
            else if (isEach)
            {
                sr.sprite = eachSlide[i];
            }
            else
            {
                sr.sprite = normalSlide[i];
            }

            sbRender.Add(sr);
            sr.color = new Color(1f, 1f, 1f, 0f);
            sr.sortingOrder += sortIndex;
            sr.sortingLayerName = "Slide";
        }
    }

    // Update is called once per frame
    private void Update()
    {
        // 组内 wifi 用组曲线自己的当前位置，主谱面的 SV 不能带它
        var startiming = (float)(timeProvider.GetScrollDistD(svGroup)
                                 - timeProvider.GetPositionAtTimeD(timeStart, svGroup));
        var realStar = (float)(timeProvider.AudioTime - timeStart);
        if (!canSVAffect) startiming = realStar;
        if (startiming <= 0f)
        {
            var alpha = startiming * (speed / 3) + 1f;
            alpha = alpha > 1f ? 1f : alpha;
            alpha = alpha < 0f ? 0f : alpha;
            setSlideBarAlpha(alpha);
            foreach (var star in star_slide)
            {
                if (star.activeSelf)
                {
                    star.SetActive(false);
                    star.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, 0f);
                    foreach (var anim in animators) anim.enabled = false;
                    startShining = false;
                }
            }
            return;
        }

        setSlideBarAlpha(1f);

        if (isBreak && !startShining)
        {
            startShining = true;
            foreach (var anim in animators)
            {
                anim.enabled = true;
                anim.Play("BreakShine", -1, 0f);
            }
        }

        foreach (var star in star_slide)
            star.SetActive(true);

        var timing = timeProvider.GetTiming(time, svGroup);
        var realtime = (float)(timeProvider.AudioTime - time);
        
        if (!canSVAffect) timing = realtime;
        // 只有真实时间到了才算"开始滑"，否则负 SV 把 timing 推成正数时 wifi 会提前跑到终点并锁死
        if (realtime >= 0f) slidingStarted = true;
        if (timing <= 0f)
        {
            // 已经滑动过的 wifi 不能因为 SV 回退而跳回起点
            if (!slidingStarted)
            {
                float alpha;
                if (isGroupPart)
                {
                    alpha = 0;
                }
                else
                {
                    alpha = 1f - -timing / (float)(time - timeStart); // time 是 double，这里只要渐入比例，转 float
                    alpha = alpha > 1f ? 1f : alpha;
                    alpha = alpha < 0.5f ? 0.5f : alpha;
                }

                for (var i = 0; i < star_slide.Length; i++)
                {
                    spriteRenderer_star[i].color = new Color(1, 1, 1, alpha);
                    star_slide[i].transform.localScale = new Vector3(alpha + 0.5f, alpha + 0.5f, alpha + 0.5f);
                    star_slide[i].transform.position = SlidePositionStart;
                }
            }
        }

        if (timing > 0f && slidingStarted)
        {
            // timing 是"位置时间"，LastFor 是"秒"，SV≠1 时两者尺度不同。
            // 必须像 SlideDrop 一样用 P(t+LastFor)-P(t) 归一化，否则 SV 下 wifi 星星会跑过头或走不完。
            var lastScroll = timeProvider.GetPositionAtTime(time + LastFor, svGroup) - timeProvider.GetPositionAtTime(time, svGroup);
            // 负 SV / SV=0 的段里 lastScroll 可能 <= 0，直接除会出 inf/NaN
            float process;
            if (Mathf.Abs(lastScroll) <= 0.0001f)
                process = realtime / Mathf.Max(LastFor, 0.0001f);
            else
                process = timing / lastScroll;
            var realPro = (LastFor - realtime) / LastFor;
            realPro = 1f - realPro;
            if (!canSVAffect) process = realPro;
            var processRaw = process;
            process = Mathf.Clamp01(process);
            // 这里原来是 if (process > 1)，但上一行 Clamp01 已经把上界钳到 1，条件永远不成立：
            // wifi 走到终点既不亮判定、也不销毁，星星就停在终点不动。
            // 用钳制前的值判断，并用真实时间比例兜底（SV 把位置进度压住时靠它收尾）。
            if (processRaw >= 1f || realPro >= 1f)
            {
                foreach (GameObject obj in slideBars)
                {
                    obj.SetActive(false);
                }

                if (isGroupPartEnd)
                {
                    if (realPro > 1)
                    {
                        if (isMine) GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().mineCount++;
                        else if (isBreak)
                            GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().breakCount++;
                        else
                            GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().slideCount++;
                    }
                    // 判定条原来亮完没人销毁，会永久留在 Notes 下面（All Perfect 的判定也因此永远不成立）。
                    // 只在第一次亮起时排一次销毁：每帧都调 Destroy(obj, t) 会把计时一直重置，永远不销毁。
                    if (slideOK != null && !slideOkScheduled)
                    {
                        slideOkScheduled = true;
                        slideOK.SetActive(true);
                        Destroy(slideOK, JudgementBarLifetime);
                    }
                }
                if (realPro > 1)
                {
                    for (var i = 0; i < star_slide.Length; i++)
                        Destroy(star_slide[i]);
                    Destroy(gameObject);
                }
            }

            var pos = (slideBars.Count - 1) * process;
            // Slide的箭头消失到哪里
            int slideAreaIndex;
            if (smoothSlideAnime)
            {
                slideAreaIndex = Mathf.Min((int)pos + 1, slideBars.Count);
            }
            else
            {
                slideAreaIndex = areaStep[(int)(process * (areaStep.Count - 1))];
            }

            for (var i = 0; i < star_slide.Length; i++)
            {
                spriteRenderer_star[i].color = Color.white;
                star_slide[i].transform.position =
                    (SlidePositionEnd[i] - SlidePositionStart) * process + SlidePositionStart; //TODO add some runhua
                star_slide[i].transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            }

            if(realtime > 0f) for (var i = 0; i < slideAreaIndex; i++) slideBars[i].SetActive(false);
        }
    }

    private void OnEnable()
    {
    }

    private void setSlideBarAlpha(float alpha)
    {
        foreach (var sr in sbRender)
        {
            var oldColor = sr.color;
            oldColor.a = alpha;
            sr.color = oldColor;
        }
    }

    private Vector3 getPositionFromDistance(float distance)
    {
        return new Vector3(
            distance * Mathf.Cos((startPosition * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((startPosition * -2f + 5f) * 0.125f * Mathf.PI));
    }

    private Vector3 getPositionFromDistance(float distance, int position)
    {
        return new Vector3(
            distance * Mathf.Cos((position * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((position * -2f + 5f) * 0.125f * Mathf.PI));
    }
}