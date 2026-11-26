using UnityEngine;

public class HoldDrop : NoteLongDrop
{
    public int startPosition = 1;
    public float speed = 1;

    public bool isEach;
    public bool isEX;
    public bool isMine;
    public bool isBreak;
    public bool canSVAffect;

    public Sprite tapSpr;
    public Sprite eachSpr;
    public Sprite exSpr;
    public Sprite breakSpr;
    public Sprite mineSpr;

    public Sprite eachLine;
    public Sprite breakLine;
    public Sprite mineLine;

    public Sprite holdEachEnd;
    public Sprite holdMineEnd;
    public Sprite holdBreakEnd;

    public RuntimeAnimatorController HoldShine;
    public RuntimeAnimatorController BreakShine;

    public GameObject holdEffect;

    public GameObject tapLine;

    public Color exEffectTap;
    public Color exEffectEach;
    public Color exEffectBreak;
    private Animator animator;

    private bool breakAnimStart;
    private SpriteRenderer exSpriteRender;
    private bool holdAnimStart;
    private SpriteRenderer holdEndRender;
    private SpriteRenderer lineSpriteRender;

    private SpriteRenderer spriteRenderer;

    private AudioTimeProvider timeProvider;

    private void Start()
    {
        var notes = GameObject.Find("Notes").transform;
        holdEffect = Instantiate(holdEffect, notes);
        holdEffect.SetActive(false);

        tapLine = Instantiate(tapLine, notes);
        tapLine.SetActive(false);
        lineSpriteRender = tapLine.GetComponent<SpriteRenderer>();

        exSpriteRender = transform.GetChild(0).GetComponent<SpriteRenderer>();

        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        holdEndRender = transform.GetChild(1).GetComponent<SpriteRenderer>();

        spriteRenderer.sortingOrder += noteSortOrder;
        exSpriteRender.sortingOrder += noteSortOrder;
        holdEndRender.sortingOrder += noteSortOrder;

        spriteRenderer.sprite = tapSpr;
        exSpriteRender.sprite = exSpr;

        var anim = gameObject.AddComponent<Animator>();
        anim.enabled = false;
        animator = anim;

        if (isEX) exSpriteRender.color = exEffectTap;
        if (isMine)
        {
            spriteRenderer.sprite = mineSpr;
            lineSpriteRender.sprite = mineLine;
            holdEndRender.sprite = holdMineEnd;
        }
        if (isEach)
        {
            spriteRenderer.sprite = eachSpr;
            lineSpriteRender.sprite = eachLine;
            holdEndRender.sprite = holdEachEnd;
            if (isEX) exSpriteRender.color = exEffectEach;
        }

        if (isBreak)
        {
            spriteRenderer.sprite = breakSpr;
            lineSpriteRender.sprite = breakLine;
            holdEndRender.sprite = holdBreakEnd;
            if (isEX) exSpriteRender.color = exEffectBreak;
        }

        spriteRenderer.forceRenderingOff = true;
        exSpriteRender.forceRenderingOff = true;
        holdEndRender.enabled = false;
        
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

        // 判定中（realtime 在 [0, LastFor] 之间）头端必须压在判定圈上（4.8 就是 realtime=0 的位置）。
        // 负 SV 回退会把整条推离判定圈，一旦两端都跑掉，画面上就看不到"正在按住"的那一端了。
        // canSVAffect=false 的 hold 本来就不跟 SV 走，保持原来的穿过表现。
        var judging = canSVAffect && realtime >= 0f && realtime <= LastFor;
        if (judging) distance = 4.8f;

        var destScale = distance * 0.4f + 0.51f;
        if (destScale < 0f)
        {
            // 越过判定圈很远（负 SV / 回退）时必须隐藏，否则长条会停在最后的可见位置一直不动
            destScale = 0f;
            spriteRenderer.forceRenderingOff = true;
            exSpriteRender.forceRenderingOff = true;
            holdEndRender.enabled = false;
            if (tapLine != null) tapLine.SetActive(false);
            if (holdEffect != null) holdEffect.SetActive(false);
            return;
        }

        spriteRenderer.forceRenderingOff = false;
        if (isEX) exSpriteRender.forceRenderingOff = false;

        if (isBreak && !breakAnimStart)
        {
            breakAnimStart = true;
            animator.runtimeAnimatorController = BreakShine;
            animator.enabled = true; // break hold闪烁
        }

        spriteRenderer.size = new Vector2(1.22f, 1.4f);

        // 尾巴位置 == 组曲线在"现在"和"尾巴时刻"的位置差。
        // 直接用组曲线的当前位置（不再从 timing 绕），组内 hold 才不会被主谱面的 SV 带偏。
        var holdTime = (float)(timeProvider.GetScrollDistD(svGroup)
                               - timeProvider.GetPositionAtTimeD(time + LastFor, svGroup));
        var holdReal = realtime - LastFor;
        var holdDistance = holdTime * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        var holdRealDistance = holdReal * speed * timeProvider.GetScaleFor(svGroup) + 4.8f;
        if (!canSVAffect)
        {
            holdTime = holdReal;
            holdDistance = holdRealDistance;
        }
        if (holdReal > 0)
        {
            if(!isMine) GameObject.Find("NoteEffects").GetComponent<NoteEffectManager>().PlayEffect(startPosition, isBreak);
            if (isMine)
                GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().mineCount++;
            else if (isBreak)
                GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().breakCount++;
            else
                GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().holdCount++;
            Destroy(tapLine);
            Destroy(holdEffect);
            Destroy(gameObject);
        }


        transform.rotation = Quaternion.Euler(0, 0, -22.5f + -45f * (startPosition - 1));
        tapLine.transform.rotation = transform.rotation;
        holdEffect.transform.position = getPositionFromDistance(4.8f);

        if (distance < 1.225f)
        {
            transform.localScale = new Vector3(destScale, destScale);
            spriteRenderer.size = new Vector2(1.22f, 1.42f);
            distance = 1.225f;
            var pos = getPositionFromDistance(distance);
            transform.position = pos;

        }
        else
        {
            if (judging)
            {
                // 判定中：只有头端钉在判定圈上，尾巴照 SV 算出来的位置走（所以会随 SV 上下动）。
                // 但尾巴不能画到音符生成位置之外：1.225 是所有下落脚本共用的"音符最远边界"
                // （distance < 1.225 就钳住），再往外只会拉出一条超出可见范围的长条。
                distance = 4.8f;
                if (holdDistance < 1.225f) holdDistance = 1.225f;
                if (!isMine) holdEffect.SetActive(true);
                startHoldShine();
            }
            else if (holdDistance < 1.225f && distance >= 4.8f && realDistance >= 4.8f) // 头到达 尾未出现
            {
                holdDistance = 1.225f;
                distance = 4.8f;
                if(!isMine) holdEffect.SetActive(true);
                startHoldShine();
            }
            else if (holdDistance < 1.225f && distance < 4.8f) // 头未到达 尾未出现
            {
                holdDistance = 1.225f;
            }
            else if (holdDistance >= 1.225f && distance >= 4.8f && realDistance >= 4.8f) // 头到达 尾出现
            {
                distance = 4.8f;
                if (!isMine) holdEffect.SetActive(true);
                startHoldShine();
            }
            // 最后一档（头未到达 尾出现）不需要额外处理：两端都按算出来的位置画

            // 负 SV / 回退段会让尾巴"超过"头。判定中尾巴可以跑到头的更外侧，
            // 也可以缩到判定圈内侧，都按两端实际跨度渲染；
            // 非判定中仍把尾巴压回头上，避免长度算成负数翻转。
            if (!judging && holdDistance > distance) holdDistance = distance;

            var top = Mathf.Max(distance, holdDistance);
            var bottom = Mathf.Min(distance, holdDistance);
            var size = top - bottom + 1.4f;
            transform.position = getPositionFromDistance((top + bottom) * 0.5f); //0.325
            spriteRenderer.size = new Vector2(1.22f, size);
            // 尾端标记跟着"离判定圈更远"的那一端走
            var tailOffset = size / 2 - 0.6825f;
            holdEndRender.transform.localPosition = new Vector3(0f, holdDistance >= distance ? tailOffset : -tailOffset);
            transform.localScale = new Vector3(1f, 1f);
        }

        // 尾端标记只看"尾巴本身在不在可见范围内"：1.225 是所有下落脚本共用的音符生成边界，
        // 尾巴比它更靠内就等于跑到屏幕外了。
        // 以前是分散在 if/else 各分支里开关，负 SV 把尾巴推回边界外时漏掉了关闭，
        // 于是那个点会一直浮在画面上不消失。
        holdEndRender.enabled = holdDistance > 1.225f && distance > 1.225f;

        var lineScale = Mathf.Abs(distance / 4.8f);
        lineScale = lineScale >= 1f ? 1f : lineScale;
        tapLine.transform.localScale = new Vector3(lineScale, lineScale, 1f);
        // 同 TapDrop：线的显隐每帧统一判断，避免负 SV 回弹跨帧时停在关闭状态
        // 线是从判定圈中心连到音符的，远处也要显示（靠 localScale 拉长），只按"是否退到太内侧"判断
        if (tapLine != null) tapLine.SetActive(destScale > 0.3f);
        exSpriteRender.size = spriteRenderer.size;
        //lineSpriteRender.color = new Color(1f, 1f, 1f, lineScale);
    }

    private void startHoldShine()
    {
        if (!holdAnimStart)
        {
            holdAnimStart = true;
            animator.runtimeAnimatorController = HoldShine;
            animator.enabled = true;
        }
    }

    private Vector3 getPositionFromDistance(float distance)
    {
        return new Vector3(
            distance * Mathf.Cos((startPosition * -2f + 5f) * 0.125f * Mathf.PI),
            distance * Mathf.Sin((startPosition * -2f + 5f) * 0.125f * Mathf.PI));
    }
}