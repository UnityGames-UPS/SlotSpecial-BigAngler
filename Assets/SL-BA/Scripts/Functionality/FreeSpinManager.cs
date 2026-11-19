using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Newtonsoft.Json;

// Free games presentation (pick screen, transition, counter, total win). SlotManager owns the flow.
public class FreeSpinManager : MonoBehaviour
{
    [Header("Pick Screen")]
    [SerializeField] private GameObject selectPanel;
    [SerializeField] private RectTransform wonTitle;
    [SerializeField] private RectTransform pickTitle;
    [SerializeField] private Button[] choiceButtons; // backend option order; each card has "SpinCount" and "Multiplier" TMP children
    [SerializeField] private float cardSlideOffset = 1100f;

    [Header("Transition")]
    [SerializeField] private ImageAnimation transition;
    [SerializeField] private float transitionSwapDelay = 0.7f; // the curtain fully covers the screen here

    [Header("Free Games Screen")]
    [SerializeField] private Image background; // shortcut: portrait background only, add the landscape one if landscape ships
    [SerializeField] private Sprite freeGamesBackground;
    [SerializeField] private GameObject counterRoot;
    [SerializeField] private TMP_Text remainingText;
    [SerializeField] private TMP_Text totalText;

    [Header("Total Win Popup")]
    [SerializeField] private CanvasGroup totalWinPopup;
    [SerializeField] private RectTransform totalWinTitle;
    [SerializeField] private RectTransform totalWinPanel;
    [SerializeField] private Image totalWinPanelImage;
    [SerializeField] private Sprite[] totalWinPanelSprites; // by choiceIndex, matching the card colours
    [SerializeField] private TMP_Text totalWinAmount;
    [SerializeField] private float totalWinHoldDuration = 3.5f;

    [Header("External References")]
    [SerializeField] private UIManager uiManager;
    [SerializeField] private AudioController audioController;

    internal int ChosenIndex { get; private set; } = -1;

    private Sprite _baseBackground;
    private RectTransform[] _cards;
    private Vector2[] _cardRest;
    private bool _awaitingChoice;

    private void Awake()
    {
        _baseBackground = background.sprite;
        _cards = System.Array.ConvertAll(choiceButtons, b => (RectTransform)b.transform);
        _cardRest = System.Array.ConvertAll(_cards, c => c.anchoredPosition);

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            int index = i;
            choiceButtons[i].onClick.AddListener(() =>
            {
                if (!_awaitingChoice) return;
                _awaitingChoice = false;
                ChosenIndex = index;
            });
        }
    }

    // "You won" pops, then the pick title and the cards slide in; waits for a card tap.
    internal IEnumerator PlayPick(List<FreeGameOption> options, double totalBet)
    {
        ChosenIndex = -1;
        selectPanel.SetActive(true);
        wonTitle.gameObject.SetActive(true);
        wonTitle.localScale = Vector3.zero;
        pickTitle.localScale = Vector3.zero;

        for (int i = 0; i < _cards.Length; i++)
        {
            _cards[i].Find("SpinCount").GetComponent<TMP_Text>().text = UIManager.ToSpriteString(options[i].spins);
            _cards[i].Find("Multiplier").GetComponent<TMP_Text>().text = UIManager.ToSpriteString(options[i].scatterValueMultiplier * totalBet);
            _cards[i].anchoredPosition = _cardRest[i] + new Vector2(cardSlideOffset, 0f);
            _cards[i].localScale = Vector3.one;
        }
        foreach (var fish in selectPanel.GetComponentsInChildren<ImageAnimation>()) fish.StartAnimation(); // Invoke stopped while the panel was hidden

        Sequence intro = DOTween.Sequence();
        intro.Append(wonTitle.DOScale(1f, 0.4f).SetEase(Ease.OutBack));
        intro.AppendInterval(0.6f);
        intro.Append(wonTitle.DOScale(0f, 0.35f).SetEase(Ease.InBack));
        intro.Append(pickTitle.DOScale(1f, 0.4f).SetEase(Ease.OutBack));
        for (int i = 0; i < _cards.Length; i++)
            intro.Insert(1.6f + i * 0.3f, _cards[i].DOAnchorPos(_cardRest[i], 0.35f).SetEase(Ease.OutBack));
        yield return intro.WaitForCompletion();

        _awaitingChoice = true;
        yield return new WaitUntil(() => ChosenIndex >= 0);
        yield return _cards[ChosenIndex].DOPunchScale(Vector3.one * 0.1f, 0.3f, 1, 0f).WaitForCompletion();
    }

    // Curtain covers the screen, the free-games look swaps in underneath, curtain clears.
    internal IEnumerator PlayEnter(FreeGamesResult fg)
    {
        bool done = false;
        transition.gameObject.SetActive(true); // before StartAnimation: Invoke doesn't run on inactive objects
        transition.onAnimationComplete = () => done = true;
        transition.StartAnimation();

        yield return new WaitForSeconds(transitionSwapDelay);
        selectPanel.SetActive(false);
        background.sprite = freeGamesBackground;
        counterRoot.SetActive(true);
        UpdateCounter(fg);

        yield return new WaitUntil(() => done);
        transition.gameObject.SetActive(false);
    }

    internal void UpdateCounter(FreeGamesResult fg)
    {
        // A retrigger spin reports 0 remaining while the new pick is pending; total - completed is what's really left.
        int remaining = fg.spinsRemaining > 0 ? fg.spinsRemaining : fg.totalFreeGamesCount - fg.completedFreeGamesCount;
        remainingText.text = UIManager.ToSpriteString(remaining);
        totalText.text = UIManager.ToSpriteString(fg.totalSpins);
    }

    // BIG WIN + TOTAL WIN popup, then a hard cut back to the base game.
    internal IEnumerator PlayExit(double totalWin, int choiceIndex)
    {
        yield return PlayTotalWin(totalWin, totalWinPanelSprites[choiceIndex], true);
        yield return new WaitForSeconds(1f); // the free-games screen lingers a beat before the cut
        counterRoot.SetActive(false);
        background.sprite = _baseBackground;
    }

    // BIG WIN + TOTAL WIN popup, shared with the fishing bonus. clearWin is false inside free spins so the running total stays.
    internal IEnumerator PlayTotalWin(double totalWin, Sprite panelSprite, bool clearWin)
    {
        totalWinAmount.text = UIManager.ToSpriteString(totalWin);
        if (panelSprite) totalWinPanelImage.sprite = panelSprite;
        totalWinTitle.localScale = Vector3.zero;
        totalWinPanel.localScale = Vector3.zero;
        totalWinPopup.alpha = 1f;
        totalWinPopup.gameObject.SetActive(true);

        audioController.PlayTotalWin();
        Sequence show = DOTween.Sequence();
        show.Append(totalWinTitle.DOScale(1f, 0.4f).SetEase(Ease.OutBack));
        show.Join(totalWinPanel.DOScale(1f, 0.4f).SetEase(Ease.OutBack));

        // Holds until the time runs out or the player taps TAKE.
        bool taken = false;
        uiManager.ShowTakeButton(() => taken = true);
        for (float t = 0f; t < 0.4f + totalWinHoldDuration && !taken; t += Time.deltaTime)
            yield return null;
        uiManager.HideTakeButton();
        show.Complete();

        if (clearWin) uiManager.UpdateWin(0); // WIN -> GOOD LUCK just before the fade, as in the clip
        yield return totalWinPopup.DOFade(0f, 0.4f).WaitForCompletion();
        totalWinPopup.gameObject.SetActive(false);
    }

    // shortcut: demo only — plays the screens without the backend, remove before shipping.
    [ContextMenu("Test Free Games UI")]
    private void TestFreeGamesUI() => StartCoroutine(TestFreeGamesRoutine());

    private IEnumerator TestFreeGamesRoutine()
    {
        var options = JsonConvert.DeserializeObject<List<FreeGameOption>>(DemoOptionsJson);
        yield return PlayPick(options, 0.1);
        int spins = options[ChosenIndex].spins;
        yield return PlayEnter(new FreeGamesResult { spinsRemaining = spins, totalSpins = spins });
        yield return new WaitForSeconds(2f);
        yield return PlayExit(0.30, ChosenIndex);
    }

    private const string DemoOptionsJson =
        @"[{""spins"":4,""scatterWeight"":15,""scatterSymbolId"":12,""scatterValueMultiplier"":50},{""spins"":8,""scatterWeight"":15,""scatterSymbolId"":13,""scatterValueMultiplier"":20},{""spins"":12,""scatterWeight"":15,""scatterSymbolId"":14,""scatterValueMultiplier"":10}]";
}
