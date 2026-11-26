using UnityEngine;

public class TouchDrop : NoteDrop
{
    // public float time;
    public float speed = 7;
    public char areaPosition;
    public bool isEach;
    public bool isFirework;
    public bool isMine;
    public bool isBreak;
    public Color breakColor = new Color(1f, 0.63f, 0.19f); // break touch 的橙色
    public bool canSVAffect;

    public int startPosition;

    public GameObject tapEffect;
    public GameObject justEffect;

    public GameObject multTouchEffect2;
    public GameObject multTouchEffect3;

    public Sprite fanNormalSprite;
    public Sprite fanEachSprite;
    public Sprite fanMineSprite;
    public Sprite fanBreakSprite; // 橙色版本，由皮肤在加载时换色相生成

    public Sprite pointNormalSprite;
    public Sprite pointEachSprite;
    public Sprite pointMineSprite;
    public Sprite pointBreakSprite;

    public Sprite justSprite;
    public Sprite justBreakSprite;

    public Sprite[] multTouchNormalSprite = new Sprite[2];
    public Sprite[] multTouchMineSprite = new Sprite[2];
    public Sprite[] multTouchEachSprite = new Sprite[2];
    public Sprite[] multTouchBreakSprite = new Sprite[2];

    public GameObject[] fans;
    private readonly SpriteRenderer[] fansSprite = new SpriteRenderer[7];
    private float displayDuration;

    private GameObject firework;
    private Animator fireworkEffect;
    private bool isStarted;
    private int layer;
    private float moveDuration;
    private MultTouchHandler multTouchHandler;

    private AudioTimeProvider timeProvider;

    private float wholeDuration;

    // Start is called before the first frame update
    private void Start()
    {
        wholeDuration = 3.209385682f * Mathf.Pow(speed, -0.9549621752f);
        moveDuration = 0.8f * wholeDuration;
        displayDuration = 0.2f * wholeDuration;
        
        var notes = GameObject.Find("Notes").transform;
        timeProvider = GameObject.Find("AudioTimeProvider").GetComponent<AudioTimeProvider>();
        multTouchHandler = GameObject.Find("MultTouchHandler").GetComponent<MultTouchHandler>();

        firework = GameObject.Find("Firework");
        fireworkEffect = firework.GetComponent<Animator>();

        for (var i = 0; i < 7; i++)
        {
            fansSprite[i] = fans[i].GetComponent<SpriteRenderer>();
            fansSprite[i].sortingOrder += noteSortOrder;
        }

        if (isMine)
        {
            SetfanSprite(fanMineSprite);
            fansSprite[4].sprite = pointMineSprite;
            fansSprite[5].sprite = multTouchMineSprite[0];
            fansSprite[6].sprite = multTouchMineSprite[1];
        }
        else if (isBreak)
        {
            // break touch 用的是换过色相的橙色贴图，不再靠 color 相乘（蓝 × 橙只会变暗）
            SetfanSprite(fanBreakSprite != null ? fanBreakSprite : fanNormalSprite);
            fansSprite[4].sprite = pointBreakSprite != null ? pointBreakSprite : pointNormalSprite;
            fansSprite[5].sprite = multTouchBreakSprite[0] != null ? multTouchBreakSprite[0] : multTouchNormalSprite[0];
            fansSprite[6].sprite = multTouchBreakSprite[1] != null ? multTouchBreakSprite[1] : multTouchNormalSprite[1];
        }
        else if (isEach)
        {
            SetfanSprite(fanEachSprite);
            fansSprite[4].sprite = pointEachSprite;
            fansSprite[5].sprite = multTouchEachSprite[0];
            fansSprite[6].sprite = multTouchEachSprite[1];
        }
        else
        {
            SetfanSprite(fanNormalSprite);
            fansSprite[4].sprite = pointNormalSprite;
            fansSprite[5].sprite = multTouchNormalSprite[0];
            fansSprite[6].sprite = multTouchNormalSprite[1];
        }

        justEffect.GetComponent<SpriteRenderer>().sprite =
            isBreak && justBreakSprite != null ? justBreakSprite : justSprite;

        transform.position = GetAreaPos(startPosition, areaPosition);
        justEffect.SetActive(false);
        SetfanColor(new Color(1f, 1f, 1f, 0f));
    }

    // Update is called once per frame
    private void Update()
    {
        var timing = timeProvider.GetTiming(time, svGroup);
        var realtime = (float)(timeProvider.AudioTime - time);

        //var timing = time;
        //var pow = Mathf.Pow(-timing * speed, 0.1f)-0.4f;
        // 扇叶张合是位置 x（= timing，判定时刻为 0）的函数，只由 x 决定。
        // 分母必须是 touch 的整个可见跨度 wholeDuration，不能用 moveDuration：
        // moveDuration 只有 0.8×wholeDuration，x 落在 [-wholeDuration, -moveDuration]
        // 这一段时已经被 clamp 到最大，前段就是一段完全水平的"张开不动"。
        // 用 wholeDuration 后，整个可见范围内 f(x) 单调，四个 C 的 x 完全相同，
        // 所以它们必然完全同步、完全一致地重复三次。
        var pow = -Mathf.Exp(8 * (timing * 0.4f / wholeDuration) - 0.85f) + 0.42f;
        var realPow = -Mathf.Exp(8 * (realtime * 0.4f / moveDuration) - 0.85f) + 0.42f;
        var distance = Mathf.Clamp(pow, 0f, 0.4f);
        var realDistance = Mathf.Clamp(realPow, 0f, 0.4f);
        if(!canSVAffect)
        {
            timing = realtime;
            pow = realPow; 
            distance = realDistance;
        }
        if (realtime > 0.05f)
        {
            multTouchHandler.cancelTouch(this);
            if(!isMine) Instantiate(tapEffect, transform.position, transform.rotation);
            if(isMine) GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().mineCount++;
            else if (isBreak) GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().breakCount++;
            else GameObject.Find("ObjectCounter").GetComponent<ObjectCounter>().touchCount++;
            if (isFirework)
            {
                fireworkEffect.SetTrigger("Fire");
                firework.transform.position = transform.position;
            }

            Destroy(gameObject);
        }
        else if(timing > 0.05f)
        {
            SetfanColor(new Color(1f, 1f, 1f, 0f));
        }
        if (realtime > 0f && !isMine) justEffect.SetActive(true);

        if (-timing <= wholeDuration && -timing > moveDuration)
        {
            if (!isStarted)
            {
                isStarted = true;
                multTouchHandler.registerTouch(this);
            }
            SetfanColor(new Color(1f, 1f, 1f, Mathf.Clamp((wholeDuration + timing) / displayDuration, 0f, 1f)));
        }
        else if (-timing < moveDuration)
        {
            if (!isStarted)
            {
                isStarted = true;
                multTouchHandler.registerTouch(this);
            }

            SetfanColor(Color.white);
        }
        else if (-timing > wholeDuration && isStarted)
        {
            isStarted = false;
            multTouchHandler.cancelTouch(this);
            SetfanColor(new Color(1f, 1f, 1f, 0f));
        }

        if (float.IsNaN(distance)) distance = 0f;
        // 判定之后不再跟着 SV 摆动：判定瞬间 SV 往回走的话，扇叶会又被推开，
        // 但那 0.05 秒里已经算打过了，保持合拢更干净。
        if (realtime > 0f) distance = 0f;
        // SC：touch 的位置固定在判定区、不跟着缩放，但"收缩"的行程要跟 SC 走——
        // 缩放变小时扇形张开的幅度也跟着变小，观感上就是收缩变慢。
        var scaledDistance = distance * timeProvider.GetScaleFor(svGroup);
        for (var i = 0; i < 4; i++)
        {
            var pos = (0.226f + scaledDistance) * GetAngle(i);
            fans[i].transform.localPosition = pos;
        }
    }

    public void setLayer(int newLayer)
    {
        layer = newLayer;
        if (layer == 1)
        {
            multTouchEffect2.SetActive(true);
            multTouchEffect3.SetActive(false);
        }
        else if (layer == 2)
        {
            multTouchEffect2.SetActive(false);
            multTouchEffect3.SetActive(true);
        }
        else
        {
            multTouchEffect2.SetActive(false);
            multTouchEffect3.SetActive(false);
        }
    }

    public void layerDown()
    {
        setLayer(layer - 1);
    }

    private Vector3 GetAngle(int index)
    {
        var angle = index * (Mathf.PI / 2);
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
        // break touch 没有专属贴图，用橙色着色；alpha 仍交给渐入渐出控制
        if (isBreak) color = new Color(breakColor.r, breakColor.g, breakColor.b, color.a);
        foreach (var fan in fansSprite) fan.color = color;
    }

    private void SetfanSprite(Sprite sprite)
    {
        for (var i = 0; i < 4; i++) fansSprite[i].sprite = sprite;
    }
}