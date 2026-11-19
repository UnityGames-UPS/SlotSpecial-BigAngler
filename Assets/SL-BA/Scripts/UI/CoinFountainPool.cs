using System.Collections;
using DG.Tweening;
using UnityEngine;

// Coin-fountain pool. The base GenericObjectPool handles queue/activate/return; this subclass adds the
// continuous burst spawning and the per-coin launch randomization that gives each coin a unique arc.
public class CoinFountainPool : GenericObjectPool<CoinFountainItem>
{
  [Header("Fountain Spawn")]
  [SerializeField] private RectTransform spawnOrigin; // WinPanelBG — coins originate here (tracks its Y)
  [SerializeField] private int coinsPerBatch = 12;    // coins launched together each wave
  [SerializeField] private float batchInterval = 0.35f; // gap between waves

  [Header("Per-Coin Randomization")]
  [SerializeField] private float coinFadeInDuration = 0.15f;
  [SerializeField] private float fallToY = -800f; // anchored Y coins fall to (set to WinPanelBG hidden Y, off-screen)
  [SerializeField] private float launchAngle = 90f;  // degrees: 0 = right, 45 = north-east, 90 = up, 135 = north-west
  [SerializeField] private float angleSpread = 80f;  // total fan width around launchAngle (0 = every coin same direction)
  [SerializeField] private Vector2 launchPowerRange = new Vector2(100f, 150f); // distance from origin to the arc's apex
  [SerializeField] private Vector2 upDurationRange = new Vector2(0.45f, 0.7f);
  [SerializeField] private Vector2 downDurationRange = new Vector2(0.5f, 0.8f);
  [SerializeField] private Ease upEase = Ease.OutQuad;
  [SerializeField] private Ease downEase = Ease.InQuad;

  private bool _running;
  private Coroutine _loop;

  // private void Start()
  // {
  //   StartFountain();
  // }

  // Begins the continuous fountain. No-op if already running.
  internal void StartFountain()
  {
    if (_running) return;
    _running = true;
    _loop = StartCoroutine(SpawnLoop());
  }

  private IEnumerator SpawnLoop()
  {
    while (_running)
    {
      SpawnBatch();
      yield return new WaitForSeconds(batchInterval);
    }
  }

  private void SpawnBatch()
  {
    if (spawnOrigin == null) return;
    Vector2 origin = spawnOrigin.anchoredPosition;

    for (int i = 0; i < coinsPerBatch; i++)
    {
      CoinFountainItem coin = GetFromPool();
      if (coin == null) continue;

      float upDur = Random.Range(upDurationRange.x, upDurationRange.y);
      float downDur = Random.Range(downDurationRange.x, downDurationRange.y);
      float angle = (launchAngle + Random.Range(-angleSpread, angleSpread) * 0.5f) * Mathf.Deg2Rad;
      float power = Random.Range(launchPowerRange.x, launchPowerRange.y);
      float riseY = Mathf.Sin(angle) * power;
      // X drifts linearly over the whole flight; scale it so the apex (reached at upDur) sits on the launch angle.
      float driftX = Mathf.Cos(angle) * power * (upDur + downDur) / upDur;

      CoinFountainItem captured = coin;
      captured.Launch(origin, coinFadeInDuration, riseY, driftX, upDur, downDur, fallToY, upEase, downEase,
        () =>
        {
          // ClearAll may have run before this arc finished; only return if still in use.
          if (ItemsInUse.Contains(captured)) ReturnToPool(captured);
        });
    }
  }

  // Stops spawning new waves; leaves in-flight coins alone.
  internal void StopFountain()
  {
    _running = false;
    if (_loop != null)
    {
      StopCoroutine(_loop);
      _loop = null;
    }
  }

  // Fades out every active coin but keeps them flipping (and the fountain keeps spawning).
  internal void FadeOutAllActive(float duration)
  {
    foreach (CoinFountainItem item in ItemsInUse)
    {
      if (item != null) item.FadeOut(duration);
    }
  }

  // Stops the fountain and returns all coins to the pool.
  internal void ClearAll()
  {
    StopFountain();
    ReturnAllItemsToPool();
  }

  internal override void ReturnToPool(CoinFountainItem item)
  {
    if (item != null) item.ResetState();
    base.ReturnToPool(item);
  }

  internal override void ReturnAllItemsToPool()
  {
    foreach (CoinFountainItem item in ItemsInUse)
    {
      if (item != null) item.ResetState();
    }
    base.ReturnAllItemsToPool();
  }
}
