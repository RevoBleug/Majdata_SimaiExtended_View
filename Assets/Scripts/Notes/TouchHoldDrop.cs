using UnityEngine;

public class TouchHoldDrop : NoteLongDrop
{
    // public float time;
    // public float LastFor = 1f;
    public float speed = 1;
    public bool isFirework;
    public bool isMine;
    public bool isEach;
    public char areaPosition;
    public int startPosition;
    public bool canSVAffect;

    public GameObject tapEffect;
    public GameObject holdEffect;

    public Sprite[] TouchHoldSprite = new Sprite[5];
    public Sprite[] TouchHoldMineSprite = new Sprite[5];
    public Sprite TouchPointSprite;
    public Sprite TouchPointMineSprite;
    public Sprite TouchPointEachSprite;

    public GameObject[] fans;
    public SpriteMask mask;
    private readonly SpriteRenderer[] fansSprite = new SpriteRenderer[6];
    private float displayDuration;

    private GameObject firework;
    private Animator fireworkEffect;
    private float moveDuration;

    private AudioTimeProvider timeProvider;

    private float wholeDuration;

    // Start is called before the first frame update
    private void Start()
    {
        wholeDuration = 3.209385682f * Mathf.Pow(speed, -0.9549621752f);
        moveDuration = 0.8f * wholeDuration;
        displayDuration = 0.2f * wholeDuration;

        var notes = GameObject.Find("Notes").transform;
        holdEffect = Instantiate(holdEffect, notes);
        holdEffect.SetActive(false);

        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();

        firework = GameObject.Find("Firework");
        fireworkEffect = firework.GetComponent<Animator>();

        for (var i = 0; i < 6; i++)
        {
            fansSprite[i] = fans[i].GetComponent<SpriteRenderer>();
            fansSprite[i].sortingOrder += noteSortOrder;
        }

        if (isMine)
        {
            for (var i = 0; i < 4; i++) fansSprite[i].sprite = TouchHoldMineSprite[i];
            fansSprite[5].sprite = TouchHoldMineSprite[4]; // TouchHold Border
            fansSprite[4].sprite = TouchPointMineSprite;
        }
        else
        {
            for (var i = 0; i < 4; i++) fansSprite[i].sprite = TouchHoldSprite[i];
            fansSprite[5].sprite = TouchHoldSprite[4]; // TouchHold Border
            if(isEach) fansSprite[4].sprite = TouchPointEachSprite;
            else fansSprite[4].sprite = TouchPointSprite;
        }
        transform.position = GetAreaPos(startPosition, areaPosition);
        SetfanColor(new Color(1f, 1f, 1f, 0f));
        mask.enabled = false;
    }

    // Update is called once per frame
    private void Update()
    {
        var timing = timeProvider.GetTiming(time, svGroup);
        var realtime = (float)(timeProvider.AudioTime - time);
        // 同 TouchDrop：扇叶张合只由位置 x 决定，分母用整个可见跨度 wholeDuration，
        // 用 moveDuration 的话 x 的前 20% 会被 clamp 成一段水平的"张开不动"。
        //var pow = Mathf.Pow(-timing * speed, 0.1f) - 0.4f;
        var pow = -Mathf.Exp(8 * (timing * 0.4f / wholeDuration) - 0.85f) + 0.42f;
        var distance = Mathf.Clamp(pow, 0f, 0.4f);
        var realPow = -Mathf.Exp(8 * (realtime * 0.4f / moveDuration) - 0.85f) + 0.42f;
        var realDistance = Mathf.Clamp(realPow, 0f, 0.4f);
        if(!canSVAffect)
        {
            timing = realtime;
            pow = realPow; 
            distance = realDistance;
        }
        // 用真实时间判断结束：SV 回退时位置时间可能永远到不了 LastFor，
        // 那样这个 touch-hold 会一直不消失、也不计数。
        if (realtime > LastFor)
        {
            Instantiate(tapEffect, transform.position, transform.rotation);
            if(isMine) GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().mineCount++;
            else GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().holdCount++;
            if (isFirework)
            {
                fireworkEffect.SetTrigger("Fire");
                firework.transform.position = transform.position;
            }

            Destroy(holdEffect);
            Destroy(gameObject);
        }

        if (-timing <= wholeDuration && -timing > moveDuration)
        {
            SetfanColor(new Color(1f, 1f, 1f, Mathf.Clamp((wholeDuration + timing) / displayDuration, 0f, 1f)));
            fans[5].SetActive(false);
            mask.enabled = false;
        }
        else if (-timing < moveDuration && (-realtime < moveDuration || realtime <= LastFor))
        {
            fans[5].SetActive(true);
            mask.enabled = true;
            SetfanColor(Color.white);
            // 外圈展开：蒙版 alpha 小于当前进度的像素不显示、大于的全显示（硬擦除，不是淡入）。
            // 进度 = 按住这段已走过的滚动距离 / 整段按住的滚动距离，两端都取本组 SV 曲线的位置，
            // 所以 <SV*0:1#4:3> 这种从 0 拉起来的段前期几乎不动、后面越走越快；SV=1 时与按真实时间线性展开等价。
            var ringStart = timeProvider.GetPositionAtTimeD(time, svGroup);
            var ringSpan = timeProvider.GetPositionAtTimeD(time + LastFor, svGroup) - ringStart;
            var realProgress = realtime / Mathf.Max(LastFor, 1e-4f);
            var ringProgress = canSVAffect && ringSpan > 1e-6
                ? (float)((timeProvider.GetScrollDistD(svGroup) - ringStart) / ringSpan)
                : realProgress;
            mask.alphaCutoff = 0.91f * Mathf.Clamp01(ringProgress);
        }
        else if(-timing > wholeDuration)
        {
            SetfanColor(new Color(1f, 1f, 1f, 0f));
            mask.enabled = false;
        }

        if (float.IsNaN(distance)) distance = 0f;
        if (distance == 0f && realDistance == 0f) holdEffect.SetActive(true);
        // SC：位置固定在判定区不动，只让扇形的收缩行程跟着 SC 缩放（观感上收缩变慢）
        var scaledDistance = distance * timeProvider.GetScaleFor(svGroup);
        for (var i = 0; i < 4; i++)
        {
            var pos = (0.226f + scaledDistance) * GetAngle(i);
            fans[i].transform.position = pos;
        }
    }

    private Vector3 GetAngle(int index)
    {
        var angle = Mathf.PI / 4 + index * (Mathf.PI / 2);
        return new Vector3(Mathf.Sin(angle), Mathf.Cos(angle));
    }

    private Vector3 GetAreaPos(int index, char area)
    {
        /// <summary>
        /// AreaDistance: 
        /// C:   0
        /// E:   3.1
        /// B:   2.21
        /// A,D: 4.8
        /// </summary>
        if (area == 'C') return Vector3.zero;
        if (area == 'B')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 5 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 2.3f;
        }

        if (area == 'A')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 5 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 4.1f;
        }

        if (area == 'E')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 6 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 3.0f;
        }

        if (area == 'D')
        {
            var angle = -index * (Mathf.PI / 4) + Mathf.PI * 6 / 8;
            return new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 4.1f;
        }

        return Vector3.zero;
    }



    private void SetfanColor(Color color)
    {
        foreach (var fan in fansSprite) fan.color = color;
    }
}