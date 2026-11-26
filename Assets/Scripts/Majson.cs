using System.Collections.Generic;

internal class Majson
{
    public string artist = "default";
    public string designer = "default";
    public string difficulty = "EZ";
    public int diffNum = 0;
    public string level = "1";
    public List<SimaiTimingPoint> timingList = new();
    public string title = "default";
}

internal class SimaiTimingPoint
{
    public float currentBpm;
    public bool havePlayed;
    public float HSpeed = 1.0f;
    public float Scale = 1.0f; // SC：谱面整体缩放
    public float ScaleTransition; // SC 渐变时长（秒）
    public string noteContent;
    public List<SimaiNote> noteList = new();
    public float SVeloc = 1.0f;
    public float SVTransition; // SV 渐变时长（秒）
    // <SV*-1:0#4:1> 的显式起始值：给出时渐变从 SVStart 开始，而不是从"当前值"接着变
    public bool hasSVStart;
    public float SVStart;
    public bool hasScaleStart;
    public float ScaleStart;
    // <GR*x> 插入的组编号，0 = 主谱面。组内音符的 SV/SC 只查自己这条曲线
    public int groupIndex;
    // 组插入点的时间（组内时间点都带，用来对齐组曲线和主谱面的位置基准）
    public double groupStartTime;
    public float groupInitSV = 1f; // 组曲线的初始 SV（组内一律从 1 起，不继承主谱面）
    public float groupInitScale = 1f; // 组曲线的初始 SC（组内一律从 1 起，不继承主谱面）
    public int rawTextPositionX;
    public int rawTextPositionY;
    public double time;
}

internal enum SimaiNoteType
{
    Tap,
    Slide,
    Hold,
    Touch,
    TouchHold
}

internal class SimaiNote
{
    public double holdTime = 0d;
    public bool isBreak = false;
    public bool isEx = false;
    public bool isMine = false;
    public bool isFakeRotate = false;
    public bool isForceStar = false;
    public bool isHanabi = false;
    public bool isSlideBreak = false;
    public bool isSlideMine = false;
    public bool isSlideNoHead = false;
    public bool canSVAffect = true;
    // 星星头单独一份：c 写在轨迹符号之前只影响头（5c-1），写在后面只影响滑条（5-1c）。
    public bool canSVAffectHead = true;

    public string noteContent; //used for star explain
    public SimaiNoteType noteType;

    public double slideStartTime = 0d;
    public double slideTime = 0d;
    public double lastSlideTime = 0d;

    public int startPosition = 1; //键位（1-8）
    public char touchArea = ' ';

    // 宽音符：从 startPosition 沿某个方向铺到 wideEndPosition（含两端），只有 Tap / Break 有
    public bool isWide = false;
    public int wideEndPosition = 0;
    public bool isWideClockwise = false;
}

internal class EditRequestjson
{
    public float audioSpeed;
    public float backgroundCover;
    public EditorComboIndicator comboStatusType;
    public EditorControlMethod control;
    public string jsonPath;
    public float noteSpeed;
    public long startAt;
    public float startTime;
    public float touchSpeed;
    public bool smoothSlideAnime;
    public bool wideNoteHollow = true; // 宽音符空心/实心，由 Edit 的编辑器设置下发
}

public enum EditorComboIndicator
{
    None,

    // List of viable indicators that won't be a static content.
    // ScoreBorder, AchievementMaxDown, ScoreDownDeluxe are static.
    Combo,
    ScoreClassic,
    AchievementClassic,
    AchievementDownClassic,
    AchievementDeluxe = 11,
    AchievementDownDeluxe,
    ScoreDeluxe,

    // Please prefix custom indicator with C
    CScoreDedeluxe = 101,
    CScoreDownDedeluxe,
    MAX
}

internal enum EditorControlMethod
{
    Start,
    Stop,
    OpStart,
    Pause,
    Continue,
    Record
}