using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using DG.Tweening;

// Fishing bonus presentation (water curtain, logo, cast, swarm, total win). SlotManager owns the flow.
public class FishingBonusManager : MonoBehaviour
{
    [Header("Intro / Outro")]
    [SerializeField] private GameObject introRoot;   // FishingBonusIntro
    [SerializeField] private RectTransform leftDoor;
    [SerializeField] private RectTransform rightDoor;
    [SerializeField] private ImageAnimation sweep;   // frame 0 is plain water, so it doubles as the backdrop behind the logo
    [SerializeField] private ImageAnimation logo;
    [SerializeField] private float doorDuration = 0.3f;
    [SerializeField] private float sweepDelay = 1.4f; // after the logo starts

    [Header("Fishing Scene")]
    [SerializeField] private GameObject sceneRoot;   // MainGame/FishingBonus
    [SerializeField] private Button hookButton;      // Boat/BonusButton
    [SerializeField] private Button startButton;     // the shared START button
    [SerializeField] private RectTransform hookGlow; // optional
    [SerializeField] private Transform fishes;
    [SerializeField] private Image whiteWash;        // LoadingScrrem/WhiteBG
    [SerializeField] private ImageAnimation loadingIcon;
    [SerializeField] private float glowDuration = 1.1f;
    [SerializeField] private float swarmDuration = 0.5f;
    [SerializeField] private float whiteFadeDuration = 0.9f;
    [SerializeField] private float loadingDuration = 1.1f;
    [SerializeField] private float popupDelay = 1f;

    [Header("Total Win")]
    [SerializeField] private Sprite totalWinPanelSprite;

    [Header("External References")]
    [SerializeField] private FreeSpinManager freeSpinManager;
    [SerializeField] private BonusWheelManager bonusWheelManager;
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AudioController audioController;

    private Vector2 _leftRest, _rightRest;
    private RectTransform[] _fish;
    private Vector2[] _fishRest;
    private Vector3[] _fishScale;
    private bool _awaitingCast;

    private void Awake()
    {
        _leftRest = leftDoor.anchoredPosition;
        _rightRest = rightDoor.anchoredPosition;

        _fish = new RectTransform[fishes.childCount];
        _fishRest = new Vector2[_fish.Length];
        _fishScale = new Vector3[_fish.Length];
        for (int i = 0; i < _fish.Length; i++)
        {
            _fish[i] = (RectTransform)fishes.GetChild(i);
            _fishRest[i] = _fish[i].anchoredPosition;
            _fishScale[i] = _fish[i].localScale;
        }

        hookButton.onClick.AddListener(OnCast);
        startButton.onClick.AddListener(OnCast);
    }

    private void OnCast()
    {
        if (!_awaitingCast) return;
        _awaitingCast = false;
    }

    internal IEnumerator Play(double winCash, bool inFreeSpins)
    {
        // Water halves close over the reels, "YOU WON FISHING BONUS" plays, hard cut to the fishing scene.
        yield return Curtain(true);
        audioController.PlayFishingBonusWon();
        yield return PlayLogo();
        ResetScene();
        sceneRoot.SetActive(true);
        introRoot.SetActive(false);

        // Cast: START or a tap on the hook. Until then the hook glow breathes in and out.
        Graphic glow = hookGlow ? hookGlow.GetComponent<Graphic>() : null;
        Tween glowPulse = null;
        if (glow)
        {
            SetAlpha(glow, 0f);
            hookGlow.gameObject.SetActive(true);
            glowPulse = glow.DOFade(1f, 0.6f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
        }
        bonusWheelManager.SetStartButtonsShown(true);
        _awaitingCast = true;
        yield return new WaitUntil(() => !_awaitingCast);
        glowPulse?.Kill(); // holds its current alpha, then fades out with the swarm

        yield return new WaitForSeconds(glowDuration);
        uiManager.SetStartButtonInteractable(false);

        // Every fish swarms the hook while the screen washes out.
        Vector3 hook = hookButton.transform.position;
        audioController.PlayFishCaught();
        Sequence swarm = DOTween.Sequence();
        for (int i = 0; i < _fish.Length; i++)
        {
            swarm.Join(_fish[i].DOMove(hook, swarmDuration).SetEase(Ease.InQuad));
            swarm.Join(_fish[i].DOScale(_fishScale[i] * 0.8f, swarmDuration));
        }
        swarm.Insert(0.1f, whiteWash.DOFade(1f, whiteFadeDuration));
        if (glow) swarm.Insert(0f, glow.DOFade(0f, swarmDuration)); // glow slowly dies as the fish close in
        yield return swarm.WaitForCompletion();
        if (hookGlow) hookGlow.gameObject.SetActive(false);

        // White swirl, then the prize lands in the WIN bar.
        loadingIcon.gameObject.SetActive(true); // before StartAnimation: Invoke doesn't run on inactive objects
        loadingIcon.StartAnimation();
        yield return new WaitForSeconds(loadingDuration);
        bonusWheelManager.PayOut(winCash);
        bonusWheelManager.SetStartButtonsShown(false);
        yield return new WaitForSeconds(0.2f);

        // Hard cut back to water, logo again, water halves open onto the reels.
        leftDoor.anchoredPosition = _leftRest;
        rightDoor.anchoredPosition = _rightRest;
        logo.gameObject.SetActive(false);
        introRoot.SetActive(true);
        ShowWater();
        sceneRoot.SetActive(false);
        loadingIcon.gameObject.SetActive(false);
        yield return PlayLogo();
        yield return Curtain(false);

        yield return new WaitForSeconds(popupDelay);
        yield return freeSpinManager.PlayTotalWin(winCash, totalWinPanelSprite, !inFreeSpins);
    }

    // Doors slide in from the screen edges to meet in the middle (close), or back out (open).
    private IEnumerator Curtain(bool close)
    {
        Vector2 leftOff = _leftRest - new Vector2(leftDoor.rect.width, 0f);
        Vector2 rightOff = _rightRest + new Vector2(rightDoor.rect.width, 0f);
        if (close)
        {
            leftDoor.anchoredPosition = leftOff;
            rightDoor.anchoredPosition = rightOff;
            sweep.gameObject.SetActive(false); // full-screen water would hide the doors
            logo.gameObject.SetActive(false);
            introRoot.SetActive(true);
        }
        else
        {
            sweep.gameObject.SetActive(false);
        }

        Sequence doors = DOTween.Sequence();
        doors.Join(leftDoor.DOAnchorPos(close ? _leftRest : leftOff, doorDuration).SetEase(Ease.Linear));
        doors.Join(rightDoor.DOAnchorPos(close ? _rightRest : rightOff, doorDuration).SetEase(Ease.Linear));
        yield return doors.WaitForCompletion();

        if (close) ShowWater();
        else introRoot.SetActive(false);
    }

    private void ShowWater()
    {
        sweep.gameObject.SetActive(true);
        sweep.StopAnimation(); // StartOnAwake would run the sweep now; hold frame 0 until PlayLogo starts it
    }

    // Logo builds, holds and fades (all baked into its frames); the water sweep crosses part-way through.
    private IEnumerator PlayLogo()
    {
        bool done = false;
        logo.gameObject.SetActive(true); // before StartAnimation: Invoke doesn't run on inactive objects
        logo.onAnimationComplete = () => done = true;
        logo.StartAnimation();
        yield return new WaitForSeconds(sweepDelay);
        sweep.StartAnimation();
        yield return new WaitUntil(() => done);
        logo.gameObject.SetActive(false);
    }

    private void ResetScene()
    {
        for (int i = 0; i < _fish.Length; i++)
        {
            _fish[i].DOKill();
            _fish[i].anchoredPosition = _fishRest[i];
            _fish[i].localScale = _fishScale[i];
        }
        Color c = whiteWash.color;
        c.a = 0f;
        whiteWash.color = c;
        loadingIcon.gameObject.SetActive(false);
        if (hookGlow) hookGlow.gameObject.SetActive(false);
    }

    // shortcut: demo only — plays the bonus without the backend, remove before shipping.
    [ContextMenu("Test Fishing Bonus")]
    private void TestFishingBonus() => StartCoroutine(Play(0.10, false));

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        Color c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }
}
