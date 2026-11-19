using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using DG.Tweening;
using System.Collections;
using Newtonsoft.Json;
using Spine.Unity;
using UnityEngine.Serialization;

public class BonusWheelManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject bonusWheelPanel; // BonusObjects — its Image is the dim, so it stays on across both wheels
    [SerializeField] private Image dimOverlay;
    [SerializeField] private GameObject bonusWheelRoot;
    [SerializeField] private RectTransform wheel;
    [SerializeField] private RectTransform innerWheel; // children "1".."13" map to slices[0..12], slice 0 at the top, clockwise
    [SerializeField] private Image wheelBorder;
    [SerializeField] private RectTransform touchToSpin;
    [SerializeField] private RectTransform touchText;
    [SerializeField] private RectTransform toSpinText;
    [SerializeField] private GameObject bubbleAnimation;
    [SerializeField] private SkeletonGraphic loopBubble; // loops behind the wheel intro and again after landing

    [Header("Spin Effects")]
    [SerializeField] private Graphic bgGlow;
    [SerializeField] private Graphic ringGlow;
    [SerializeField] private GameObject landParticles;
    [SerializeField] private Image glowPulse; // GlowRing — grows from the wheel centre and fades, on repeat, from spin start to exit

    [Header("Start Buttons (bottom bar)")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button startButtonPortrait;

    [Header("Intro Tuning")]
    [SerializeField] private float dimAlpha = 0.7f;
    [SerializeField] private float idleRotateDuration = 20f;
    [SerializeField] private float wheelEnterOffset = 1200f;
    [SerializeField] private float tiltAngle = 6f;
    [SerializeField] private float hopHeight = 25f;
    [SerializeField] private float borderStartAngle = -180f;
    [SerializeField] private float textSlideOffset = 900f;
    [SerializeField] private float bubbleLead = 0.15f;        // bubbles pop just before the wheel rises
    [SerializeField] private float wheelRiseDuration = 0.5f;
    [SerializeField] private float borderDelay = 0.7f;        // wheel sits bare before the border grows
    [SerializeField] private float borderDuration = 0.6f;

    [Header("Spin Tuning")]
    [SerializeField] private float spinDuration = 6.5f;
    [SerializeField] private int fullTurns = 5;
    [SerializeField] private int borderTurns = 2;
    [SerializeField] private float overshootAngle = 8f;
    [SerializeField] private float resultHoldDuration = 2.8f;
    [SerializeField] private float glowPulseScale = 9f;      // GlowRing rect is 100px, so ~9 covers the wheel
    [SerializeField] private float glowPulseInterval = 3f;
    [SerializeField] private float wheelExitDuration = 0.9f;

    [Header("Instant Win")]
    [SerializeField] private TMP_Text instantWinText;
    [SerializeField, FormerlySerializedAs("coinFountain")] private CoinFountainPool leftCoinFountain;
    [SerializeField] private CoinFountainPool rightCoinFountain;
    [SerializeField] private RectTransform winFlyTarget;
    [SerializeField] private RectTransform winFlyTargetPortrait;
    [SerializeField] private float winCountDuration = 1f;
    [SerializeField] private float winFlyDuration = 0.6f;

    [Header("Progressive Wheel")]
    [SerializeField] private GameObject progressiveRoot;
    [SerializeField] private CanvasGroup progressiveWheelGroup;
    [SerializeField] private RectTransform progressiveInnerWheel; // slice art baked in: slice 0 at the top, clockwise
    [SerializeField] private GameObject progressiveBgShine;
    [SerializeField] private ImageAnimation progressiveInnerLights;
    [SerializeField] private RectTransform diamondPrize;
    [SerializeField] private RectTransform goldPrize;
    [SerializeField] private RectTransform silverPrize;
    [SerializeField] private CanvasGroup progressiveResult;
    [SerializeField] private Image progressiveResultTitle;
    [SerializeField] private RectTransform progressiveResultBand;
    [SerializeField] private TMP_Text progressiveResultValue;
    [SerializeField] private SkeletonGraphic[] progressiveBubbles;  // Left/RightBubbleAnimation — pop once as the panels slide in
    [SerializeField] private SkeletonGraphic progressiveParticles;  // Wheel/ParticleEffect — loops from spin start
    [SerializeField] private SkeletonGraphic progressiveResultRays; // ResultPanel/BGANIMATION — loops once the amount band opens
    [SerializeField] private Sprite[] progressiveTitleSprites;      // by prize: silver, gold, diamond
    [SerializeField] private Sprite[] progressiveBandSprites;       // by prize: silver, gold, diamond
    [SerializeField] private float resultSlideOffset = 1100f;       // ResultPanel is 784 wide, so this starts it fully off the left edge
    [SerializeField] private float progressiveLandHold = 2f;        // segment glow blinks on the PROGRESSIVE slice before the hand-over
    [SerializeField] private float progressiveFadeInDuration = 0.8f;
    [SerializeField] private float panelSlideOffset = 700f;
    [SerializeField] private float progressiveSpinDuration = 8.5f;
    [SerializeField] private float progressiveShineFadeIn = 1.2f;
    [SerializeField] private int progressiveTurns = 4;
    [SerializeField] private float progressiveHoldDuration = 2.5f;

    [Header("Demo")]
    // shortcut: demo only — Start() plays this backend prizeWheelResult on scene load, remove before shipping.
    [SerializeField, TextArea(3, 12)] private string demoResultJson = DefaultDemoJson;

    [Header("External References")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AudioController audioController;

    // Panels slide in/out bottom-first, as in the clip; keys match ProgressivePrizeWheel.prizes / prizeName.
    private static readonly string[] PrizeKeys = { "silver", "gold", "diamond" };

    private Tween _idleRotateTween;
    private Tween _touchPulseTween;
    private Tween _ringPulseTween;
    private Tween _glowPulseTween;
    private Vector2 _wheelRest, _touchRest, _toSpinRest, _winTextRest, _resultRest;
    private RectTransform[] _prizePanels;
    private Vector2[] _prizePanelRest;
    private bool _awaitingTap;
    private bool _spinRequested;
    private SkeletonGraphic _bubble, _bgShine, _stars, _progBgShine;

    private void Awake()
    {
        _wheelRest = wheel.anchoredPosition;
        _touchRest = touchText.anchoredPosition;
        _toSpinRest = toSpinText.anchoredPosition;
        _winTextRest = instantWinText.rectTransform.anchoredPosition;
        _resultRest = ((RectTransform)progressiveResult.transform).anchoredPosition;
        _prizePanels = new[] { silverPrize, goldPrize, diamondPrize };
        _prizePanelRest = System.Array.ConvertAll(_prizePanels, p => p.anchoredPosition);
        _bubble = bubbleAnimation.GetComponent<SkeletonGraphic>();
        _bgShine = bgGlow as SkeletonGraphic;
        if (landParticles) _stars = landParticles.GetComponent<SkeletonGraphic>();
        _progBgShine = progressiveBgShine.GetComponent<SkeletonGraphic>();

        touchToSpin.GetComponent<Button>().onClick.AddListener(OnSpinTapped);
        if (startButton) startButton.onClick.AddListener(OnSpinTapped);
        if (startButtonPortrait) startButtonPortrait.onClick.AddListener(OnSpinTapped);
    }

    // shortcut: demo only — remove before shipping.
    //private void Start() => StartCoroutine(PlayBonusWheel(DemoResult()));

    private PrizeWheelResult DemoResult() => JsonConvert.DeserializeObject<PrizeWheelResult>(demoResultJson);

    internal void InitializeSliceValues(List<WheelSlice> slices)
    {
        if (slices == null || innerWheel == null) return;

        for (int i = 0; i < slices.Count && i < innerWheel.childCount; i++)
        {
            if (slices[i].type != "instant_win") continue;

            TMP_Text text = innerWheel.GetChild(i).GetComponentInChildren<TMP_Text>(true);
            if (text) text.text = slices[i].value.ToString("0.##");
        }
    }

    // Full wheel: intro → wait for Touch to Spin / START → spin to result slice → result → exit.
    internal IEnumerator PlayBonusWheel(PrizeWheelResult result)
    {
        yield return PlayTriggerIntro();
        yield return WaitForStart();
        yield return SpinToSlice(result.sliceIndex);

        // Landed: one bubble pop, the loop bubble returns and the pointer segment starts blinking; stars, rays and glow pulse keep running.
        PlaySpine(_bubble, false);
        PlaySpine(loopBubble, true);
        SetAlpha(ringGlow, 0f);
        ringGlow.gameObject.SetActive(true);
        _ringPulseTween = ringGlow.DOFade(1f, 0.5f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);

        if (result.type == "progressive")
        {
            yield return new WaitForSeconds(progressiveLandHold); // segment glow blinks on PROGRESSIVE before the hand-over
            yield return ExitBonusWheel().WaitForCompletion();
            yield return PlayProgressiveWheel(result.progressivePrizeWheel);
        }
        else
        {
            if (result.type == "instant_win") yield return PlayInstantWin(result.winCash);
            yield return new WaitForSeconds(resultHoldDuration);
            ExitBonusWheel();
            yield return new WaitForSeconds(Mathf.Max(0f, wheelExitDuration - 0.5f)); // dim fade (0.5s) ends with the shrink
        }

        yield return CloseBonusObjects().WaitForCompletion();
    }

    internal IEnumerator PlayTriggerIntro()
    {
        KillLoopTweens();
        _awaitingTap = false;
        _spinRequested = false;

        bonusWheelPanel.SetActive(true);
        bonusWheelRoot.SetActive(true);
        bonusWheelRoot.transform.localScale = Vector3.one;
        progressiveRoot.SetActive(false);
        bubbleAnimation.SetActive(false);
        HideSpine(loopBubble);
        if (landParticles) landParticles.SetActive(false);
        SetStartButtonsShown(false);
        SetAlpha(dimOverlay, 0f);
        SetAlpha(bgGlow, 0f);
        SetAlpha(ringGlow, 0f);
        bgGlow.gameObject.SetActive(false);
        ringGlow.gameObject.SetActive(false);
        if (glowPulse) glowPulse.gameObject.SetActive(false);
        instantWinText.gameObject.SetActive(false);
        wheel.anchoredPosition = _wheelRest - new Vector2(0f, wheelEnterOffset);
        wheel.localEulerAngles = Vector3.zero;
        wheel.localScale = Vector3.one;
        innerWheel.localEulerAngles = Vector3.zero;
        wheelBorder.transform.localScale = Vector3.zero;
        wheelBorder.transform.localEulerAngles = new Vector3(0f, 0f, borderStartAngle);
        SetAlpha(wheelBorder, 1f);
        touchToSpin.localScale = Vector3.one;
        touchText.anchoredPosition = _touchRest - new Vector2(textSlideOffset, 0f);
        toSpinText.anchoredPosition = _toSpinRest + new Vector2(textSlideOffset, 0f);

        Sequence seq = DOTween.Sequence();
        seq.Append(dimOverlay.DOFade(dimAlpha, 0.15f));
        seq.AppendCallback(() => { if (uiManager.gameLogoObject) uiManager.gameLogoObject.SetActive(false); });
        seq.AppendCallback(() => PlaySpine(_bubble, false));
        seq.AppendCallback(() => PlaySpine(loopBubble, true));
        seq.AppendCallback(audioController.PlayWheelAppear);
        seq.AppendInterval(bubbleLead);

        // Wheel glides up from below, then tilts left/right with a small hop to settle.
        seq.Append(wheel.DOAnchorPos(_wheelRest, wheelRiseDuration).SetEase(Ease.OutSine));
        seq.Append(wheel.DOLocalRotate(new Vector3(0f, 0f, tiltAngle), 0.3f));
        seq.Join(wheel.DOAnchorPosY(_wheelRest.y + hopHeight, 0.12f).SetEase(Ease.OutQuad));
        seq.Append(wheel.DOLocalRotate(new Vector3(0f, 0f, -tiltAngle * 0.6f), 0.3f));
        seq.Join(wheel.DOAnchorPosY(_wheelRest.y, 0.12f).SetEase(Ease.InQuad));
        seq.Append(wheel.DOLocalRotate(Vector3.zero, 0.15f));

        // Border "opens" after a beat: grows straight to size (no overshoot) while unwinding back to 0°.
        seq.AppendInterval(borderDelay);
        seq.AppendCallback(() => HideSpine(loopBubble)); // stops just before the border opens
        seq.Append(wheelBorder.transform.DOScale(1f, borderDuration).SetEase(Ease.OutCubic));
        seq.Join(wheelBorder.transform.DOLocalRotate(Vector3.zero, borderDuration, RotateMode.FastBeyond360).SetEase(Ease.OutCubic));

        // "Touch" from the left, "To Spin" from the right.
        seq.AppendCallback(audioController.PlayTouchToSpin);
        seq.Append(touchText.DOAnchorPos(_touchRest, 0.35f).SetEase(Ease.OutBack));
        seq.Join(toSpinText.DOAnchorPos(_toSpinRest, 0.35f).SetEase(Ease.OutBack));

        yield return seq.WaitForCompletion();

        _idleRotateTween = innerWheel
            .DOLocalRotate(new Vector3(0f, 0f, -360f), idleRotateDuration, RotateMode.FastBeyond360)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Incremental);
        _touchPulseTween = touchToSpin
            .DOScale(1.08f, 0.6f)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    // Re-enabling a SkeletonGraphic doesn't replay its starting anim, so restart it from frame 0 (same as SpineAnimController.Restart).
    private static void PlaySpine(SkeletonGraphic sg, bool loop)
    {
        if (!sg) return;
        sg.gameObject.SetActive(true);
        if (!sg.IsValid) sg.Initialize(false);
        sg.AnimationState.ClearTracks();
        sg.Skeleton.SetToSetupPose();
        var entry = sg.AnimationState.SetAnimation(0, sg.startingAnimation, loop);
        if (!loop) entry.Complete += _ => sg.gameObject.SetActive(false); // one-shots switch off when done
        sg.Update(0f); // pose frame 0 now, so the first rendered frame isn't the setup pose (all bubbles at full alpha)
        sg.UpdateMesh();
    }

    private static void HideSpine(SkeletonGraphic sg)
    {
        if (sg) sg.gameObject.SetActive(false);
    }

    // Glow grows from the wheel centre while fading out, then waits; repeats until the wheel exits.
    private void StartGlowPulse()
    {
        if (!glowPulse) return;
        RectTransform rt = glowPulse.rectTransform;
        rt.localScale = Vector3.zero;
        SetAlpha(glowPulse, 1f);
        glowPulse.gameObject.SetActive(true);

        _glowPulseTween = DOTween.Sequence()
            .Append(rt.DOScale(glowPulseScale, 1f).SetEase(Ease.OutQuad))
            .Insert(0.7f, glowPulse.DOFade(0f, 0.3f))
            .AppendInterval(Mathf.Max(0f, glowPulseInterval - 0.6f))
            .SetLoops(-1, LoopType.Restart);
    }

    // START (or Touch to Spin) replaces the disabled SPIN button until the player taps. Also used to start free games.
    internal IEnumerator WaitForStart()
    {
        _spinRequested = false;
        SetStartButtonsShown(true);
        _awaitingTap = true;
        yield return new WaitUntil(() => _spinRequested);
        SetStartButtonsShown(false);
    }

    private IEnumerator SpinToSlice(int sliceIndex)
    {
        KillLoopTweens();

        touchToSpin.DOScale(0f, 0.4f).SetEase(Ease.InBack);
        PlaySpine(_bgShine, true);
        bgGlow.DOFade(1f, 0.4f);
        PlaySpine(_stars, true);
        StartGlowPulse();
        audioController.PlayWheelSpinning();

        // Slice i sits at -i*sliceAngle, so the wheel needs z ≡ i*sliceAngle to bring it under the top pointer.
        float sliceAngle = 360f / innerWheel.childCount;
        float target = sliceIndex * sliceAngle;
        float current = innerWheel.localEulerAngles.z;
        float end = current - Mathf.Repeat(current - target, 360f) - 360f * fullTurns; // clockwise = decreasing z

        Sequence seq = DOTween.Sequence();
        seq.Append(innerWheel.DOLocalRotate(new Vector3(0f, 0f, end - overshootAngle), spinDuration, RotateMode.FastBeyond360).SetEase(Ease.OutCubic));
        seq.Join(wheelBorder.transform.DOLocalRotate(new Vector3(0f, 0f, -360f * borderTurns), spinDuration, RotateMode.FastBeyond360).SetEase(Ease.OutCubic));
        seq.Append(innerWheel.DOLocalRotate(new Vector3(0f, 0f, end), 0.4f).SetEase(Ease.InOutSine));

        yield return seq.WaitForCompletion();
        audioController.PlayWheelStop();
    }

    // Amount pops at the wheel center, counts up while coins fountain, then flies into the bottom-bar WIN field.
    private IEnumerator PlayInstantWin(double winCash)
    {
        RectTransform textRT = instantWinText.rectTransform;
        RectTransform target = winFlyTarget && winFlyTarget.gameObject.activeInHierarchy ? winFlyTarget : winFlyTargetPortrait;

        textRT.anchoredPosition = _winTextRest;
        textRT.localScale = Vector3.zero;
        instantWinText.text = UIManager.ToSpriteString(0.0);
        instantWinText.gameObject.SetActive(true);
        leftCoinFountain.StartFountain();
        rightCoinFountain.StartFountain();

        Sequence seq = DOTween.Sequence();
        seq.Append(textRT.DOScale(1f, 0.3f).SetEase(Ease.OutBack));
        seq.Append(DOVirtual.Float(0f, (float)winCash, winCountDuration, v => instantWinText.text = UIManager.ToSpriteString(v)));
        seq.AppendCallback(() => instantWinText.text = UIManager.ToSpriteString(winCash));
        seq.AppendInterval(0.3f);
        seq.Append(textRT.DOMove(target.position, winFlyDuration).SetEase(Ease.InQuad));
        seq.Join(textRT.DOScale(0.3f, winFlyDuration).SetEase(Ease.InQuad));
        seq.AppendCallback(() =>
        {
            instantWinText.gameObject.SetActive(false);
            PayOut(winCash);
        });

        yield return seq.WaitForCompletion();
    }

    // Progressive wheel fades in, prize panels slide in, START spins it to pw.sliceIndex (bottom pointer),
    // panels slide out, then the won prize's title + amount are shown and paid.
    private IEnumerator PlayProgressiveWheel(ProgressivePrizeWheel pw)
    {
        Vector2 offscreen = new Vector2(panelSlideOffset, 0f);

        // These start active in the scene; switch them off before the root wakes so they don't autoplay at the fade-in.
        foreach (var bubble in progressiveBubbles) HideSpine(bubble);
        HideSpine(progressiveParticles);
        HideSpine(progressiveResultRays);
        progressiveRoot.SetActive(true);
        progressiveWheelGroup.alpha = 0f;
        progressiveInnerWheel.localEulerAngles = Vector3.zero;
        progressiveBgShine.SetActive(false);
        progressiveResult.gameObject.SetActive(false);
        for (int i = 0; i < _prizePanels.Length; i++)
        {
            if (pw.prizes != null && pw.prizes.TryGetValue(PrizeKeys[i], out double prize))
                _prizePanels[i].GetComponentInChildren<TMP_Text>(true).text = prize.ToString("F2");
            _prizePanels[i].anchoredPosition = _prizePanelRest[i] + offscreen;
        }
        SetStartButtonsShown(true); // the clip shows START as soon as the wheel fades in

        Sequence intro = DOTween.Sequence();
        intro.Append(progressiveWheelGroup.DOFade(1f, progressiveFadeInDuration));
        for (int i = 0; i < _prizePanels.Length; i++)
        {
            Tween slideIn = _prizePanels[i].DOAnchorPos(_prizePanelRest[i], 0.4f).SetEase(Ease.OutBack);
            if (i == 0) slideIn.OnStart(() => { foreach (var bubble in progressiveBubbles) PlaySpine(bubble, false); }); // bubbles start with SILVER
            intro.Insert(2f + i * 0.35f, slideIn);
        }
        yield return intro.WaitForCompletion();

        yield return WaitForStart();

        SetAlpha(_progBgShine, 0f);
        PlaySpine(_progBgShine, true);
        _progBgShine.DOFade(1f, progressiveShineFadeIn).SetEase(Ease.InQuad); // rays build up as the wheel spins up
        PlaySpine(progressiveParticles, true);
        progressiveInnerLights.StartAnimation();
        audioController.PlayWheelSpinning();

        // Slice i sits at -i*sliceAngle; the pointer is at the bottom, so the wheel needs z ≡ 180 + i*sliceAngle.
        float sliceAngle = 360f / pw.slices.Count;
        float target = 180f + pw.sliceIndex * sliceAngle;
        float current = progressiveInnerWheel.localEulerAngles.z;
        float end = current - Mathf.Repeat(current - target, 360f) - 360f * progressiveTurns; // clockwise
        yield return progressiveInnerWheel
            .DOLocalRotate(new Vector3(0f, 0f, end), progressiveSpinDuration, RotateMode.FastBeyond360)
            .SetEase(Ease.OutQuart)
            .WaitForCompletion();
        audioController.PlayWheelStop();

        Sequence panelsOut = DOTween.Sequence();
        for (int i = 0; i < _prizePanels.Length; i++)
            panelsOut.Insert(i * 0.3f, _prizePanels[i].DOAnchorPos(_prizePanelRest[i] + offscreen, 0.3f).SetEase(Ease.InBack));
        yield return panelsOut.WaitForCompletion();

        // Per-prize art from the Inspector; an unassigned slot keeps the scene sprite.
        int prizeIndex = Mathf.Max(0, System.Array.IndexOf(PrizeKeys, pw.prizeName));
        if (prizeIndex < progressiveTitleSprites.Length && progressiveTitleSprites[prizeIndex]) progressiveResultTitle.sprite = progressiveTitleSprites[prizeIndex];
        if (prizeIndex < progressiveBandSprites.Length && progressiveBandSprites[prizeIndex]) progressiveResultBand.GetComponent<Image>().sprite = progressiveBandSprites[prizeIndex];
        progressiveResultValue.text = pw.winCash.ToString("F2");

        RectTransform resultPanel = (RectTransform)progressiveResult.transform;
        resultPanel.anchoredPosition = _resultRest - new Vector2(resultSlideOffset, 0f);
        progressiveResultBand.localScale = new Vector3(0f, 1f, 1f);
        progressiveResult.alpha = 1f;
        progressiveResult.gameObject.SetActive(true);

        // Result panel slides in from the left; once it lands, the amount band opens from the middle with the red rays behind it.
        Sequence reveal = DOTween.Sequence();
        reveal.Append(resultPanel.DOAnchorPos(_resultRest, 1.2f).SetEase(Ease.OutCubic));
        reveal.AppendInterval(0.3f);
        reveal.AppendCallback(() => PlaySpine(progressiveResultRays, true));
        reveal.Append(progressiveResultBand.DOScaleX(1f, 0.8f).SetEase(Ease.OutCubic));
        reveal.AppendCallback(() => PayOut(pw.winCash));
        yield return reveal.WaitForCompletion();

        yield return new WaitForSeconds(progressiveHoldDuration);
    }

    internal void PayOut(double winCash)
    {
        // Adds to the WIN bar: the free-games running total, or 0 in the base game (reset at spin start).
        uiManager.UpdateWin(uiManager.CurrentWinDisplay + winCash, true);
        uiManager.UpdateBalance(uiManager.currentBalance + winCash, true);
    }

    // Shrinks the bonus wheel away; the dim (BonusObjects) stays on so the progressive wheel can follow.
    private Sequence ExitBonusWheel()
    {
        _ringPulseTween?.Kill(); // so the fade below owns it; the glow pulse keeps going while the wheel shrinks
        _awaitingTap = false;
        SetStartButtonsShown(false);
        instantWinText.gameObject.SetActive(false);

        Sequence seq = DOTween.Sequence();
        // Root, so the pointer and every glow shrink with the wheel; smooth shrink to its centre, no pop.
        seq.Append(bonusWheelRoot.transform.DOScale(0f, wheelExitDuration).SetEase(Ease.InSine));
        seq.Join(bgGlow.DOFade(0f, 0.1f));
        seq.Join(ringGlow.DOFade(0f, 0.1f));
        seq.OnComplete(() =>
        {
            KillLoopTweens();
            if (landParticles) landParticles.SetActive(false);
            HideSpine(loopBubble);
            bonusWheelRoot.SetActive(false);
        });
        return seq;
    }

    // Fades the dim and the progressive wheel out and returns to the base game.
    private Sequence CloseBonusObjects()
    {
        leftCoinFountain.StopFountain(); // coins already in the air finish their arc
        rightCoinFountain.StopFountain();

        Sequence seq = DOTween.Sequence();
        seq.Append(dimOverlay.DOFade(0f, 0.5f));
        seq.Join(progressiveWheelGroup.DOFade(0f, 0.5f));
        seq.Join(progressiveResult.DOFade(0f, 0.5f));
        seq.OnComplete(() =>
        {
            progressiveInnerLights.StopAnimation();
            progressiveBgShine.SetActive(false);
            progressiveRoot.SetActive(false);
            bubbleAnimation.SetActive(false);
            HideSpine(loopBubble);
            leftCoinFountain.ClearAll(); // coins still in flight would freeze when the panel switches off
            rightCoinFountain.ClearAll();
            bonusWheelPanel.SetActive(false);
            if (uiManager.gameLogoObject) uiManager.gameLogoObject.SetActive(true);
        });
        return seq;
    }

    private void OnSpinTapped()
    {
        if (!_awaitingTap) return;
        _awaitingTap = false;
        _spinRequested = true;
    }

    internal void SetStartButtonsShown(bool shown)
    {
        // free spins leave this shared button disabled as a placeholder, so re-enable it whenever it's shown
        if (startButton) { startButton.gameObject.SetActive(shown); startButton.interactable = true; }
        if (startButtonPortrait) { startButtonPortrait.gameObject.SetActive(shown); startButtonPortrait.interactable = true; }
        if (uiManager.spinButton) uiManager.spinButton.gameObject.SetActive(!shown);
        if (uiManager.spinButtonPortrait) uiManager.spinButtonPortrait.gameObject.SetActive(!shown);
    }

    private void KillLoopTweens()
    {
        _idleRotateTween?.Kill();
        _touchPulseTween?.Kill();
        _ringPulseTween?.Kill();
        _glowPulseTween?.Kill();
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    [ContextMenu("Test Bonus Wheel")]
    private void TestBonusWheel() => StartCoroutine(PlayBonusWheel(DemoResult()));

    [ContextMenu("Test Hide Wheel")]
    private void TestHideWheel()
    {
        ExitBonusWheel();
        CloseBonusObjects();
    }

    // prizeWheelResult from a real progressive ResultData (bonus slice 1 → progressive slice 7, silver 0.10).
    private const string DefaultDemoJson =
        @"{""sliceIndex"":1,""type"":""progressive"",""value"":1,""winCash"":0.1,""progressivePrizeWheel"":{""triggered"":true,""sliceIndex"":7,""prizeName"":""silver"",""multiplier"":1,""winCash"":0.1,""prizes"":{""gold"":0.5,""silver"":0.1,""diamond"":1},""slices"":[{""name"":""gold"",""type"":""gold"",""value"":0.5,""multiplier"":5},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""gold"",""type"":""gold"",""value"":0.5,""multiplier"":5},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""diamond"",""type"":""diamond"",""value"":1,""multiplier"":10},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""gold"",""type"":""gold"",""value"":0.5,""multiplier"":5},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1},{""name"":""silver"",""type"":""silver"",""value"":0.1,""multiplier"":1}]}}";
}
