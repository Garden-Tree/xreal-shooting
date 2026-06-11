using System.Collections;
using UnityEngine;
using Unity.XR.XREAL.Samples;

public enum TargetMovementPattern
{
    Straight, // 直線
    Zigzag,   // ジグザグ
    Spiral    // 螺旋
}

/// <summary>
/// 的（ターゲット）オブジェクトを制御するスクリプト。
/// プレイヤーに向かって移動し、被弾時にはSEとパーティクルを再生した後に非アクティブ化されます。
/// オブジェクトプール方式で再利用されます。
/// </summary>
[RequireComponent(typeof(Collider))]
public class TargetObject : MonoBehaviour
{
    [Header("表示設定")]
    [Tooltip("表示制御を行うレンダラー。未設定の場合は自動で取得します。")]
    [SerializeField]
    private Renderer m_TargetRenderer;

    [Header("被弾時エフェクト設定")]
    [Tooltip("被弾時に再生するパーティクルシステム（任意）")]
    [SerializeField]
    private ParticleSystem m_HitEffect;

    [Tooltip("被弾時に再生するオーディオソース。アタッチされていない場合は自動で追加・設定されます。")]
    [SerializeField]
    private AudioSource m_AudioSource;

    [Tooltip("被弾時に再生する効果音（SE）")]
    [SerializeField]
    private AudioClip m_HitSound;

    [Tooltip("被弾してからオブジェクトを非アクティブにするまでのディレイ秒数")]
    [SerializeField]
    private float m_DestroyDelay = 2.0f;

    [Header("移動設定")]
    [Tooltip("的の移動速度")]
    [SerializeField]
    private float m_MoveSpeed = 2.0f;

    private Collider m_Collider;
    private bool m_IsHit = false;

    private Vector3 m_InitialPosition;
    private Quaternion m_InitialRotation;
    private bool m_HasSavedInitialTransform = false;

    // 移動制御用変数
    private Vector3 m_SpawnPosition;
    private TargetMovementPattern m_ActivePattern;
    private float m_LifeTime = 0f;
    private float m_WaveFrequency = 3.0f;
    private float m_WaveAmplitude = 1.0f;
    private bool m_IsMoving = false;

    /// <summary>
    /// 被弾状態（撃破済みか）を取得します。
    /// </summary>
    public bool IsHit => m_IsHit;

    private void Awake()
    {
        if (m_TargetRenderer == null) m_TargetRenderer = GetComponent<Renderer>();
        if (m_AudioSource == null) m_AudioSource = gameObject.AddComponent<AudioSource>();
        m_Collider = GetComponent<Collider>();
        SaveInitialTransform();
    }

    private void SaveInitialTransform()
    {
        if (!m_HasSavedInitialTransform)
        {
            m_InitialPosition = transform.position;
            m_InitialRotation = transform.rotation;
            m_HasSavedInitialTransform = true;
        }
    }

    private void Update()
    {
        // 移動中でない、またはすでに被弾している場合は何もしない
        if (!m_IsMoving || m_IsHit) return;

        m_LifeTime += Time.deltaTime;

        // プレイヤー（カメラ）の位置（エディタ・実機共通でCamera.mainを基準にする）
        Vector3 playerPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Vector3 toPlayer = playerPos - m_SpawnPosition;
        float totalDistance = toPlayer.magnitude;

        if (totalDistance > 0.01f)
        {
            Vector3 forwardDir = toPlayer.normalized;

            // 進行度 t (0.0 ～ 1.0) を算出
            float t = (m_MoveSpeed * m_LifeTime) / totalDistance;
            t = Mathf.Clamp01(t);

            // 基本の直線上での現在位置
            Vector3 basePosition = Vector3.Lerp(m_SpawnPosition, playerPos, t);
            Vector3 finalPosition = basePosition;

            // 移動パターンに応じたオフセットを追加
            if (m_ActivePattern == TargetMovementPattern.Zigzag)
            {
                // プレイヤーに向かう方向と垂直な方向（横方向）にサイン波を加える
                Vector3 rightDir = Vector3.Cross(forwardDir, Vector3.up).normalized;
                if (rightDir == Vector3.zero) rightDir = Vector3.right;

                float offset = Mathf.Sin(m_LifeTime * m_WaveFrequency) * m_WaveAmplitude;
                finalPosition += rightDir * offset;
            }
            else if (m_ActivePattern == TargetMovementPattern.Spiral)
            {
                // プレイヤーに向かう方向の周りを回転するオフセットを加える（近づくにつれて収束）
                Vector3 rightDir = Vector3.Cross(forwardDir, Vector3.up).normalized;
                if (rightDir == Vector3.zero) rightDir = Vector3.right;
                Vector3 upDir = Vector3.Cross(rightDir, forwardDir).normalized;

                float angle = m_LifeTime * m_WaveFrequency * 2.0f;
                float radius = m_WaveAmplitude * (1.0f - t); // 近づくにつれて半径を絞る

                Vector3 offset = (rightDir * Mathf.Cos(angle) + upDir * Mathf.Sin(angle)) * radius;
                finalPosition += offset;
            }

            transform.position = finalPosition;

            // プレイヤーとの距離判定（旅程の 80% 以上を進んでおり、かつ距離が 1.5m 以下になったら到達と判定）
            if (t >= 0.80f && Vector3.Distance(transform.position, playerPos) < 1.5f)
            {
                ReachPlayer();
            }
        }
    }

    /// <summary>
    /// 被弾（射撃命中）したときに呼び出されます。
    /// </summary>
    public void OnHit()
    {
        if (m_IsHit) return; // 二重被弾防止
        Deactivate();

        GameManager.Instance.AddScore(100);

        // 1. パーティクルエフェクトの再生（再利用のため複製して実行）
        if (m_HitEffect != null)
        {
            ParticleSystem effectInstance = Instantiate(m_HitEffect, transform.position, transform.rotation);
            effectInstance.gameObject.SetActive(true);
            effectInstance.Play();
            Destroy(effectInstance.gameObject, m_DestroyDelay);
        }

        // 2. 効果音の再生
        if (m_HitSound != null) m_AudioSource.PlayOneShot(m_HitSound);

        // 3. 指定秒数後にこのオブジェクトを非アクティブ化 (Destroyはせず再利用)
        StartCoroutine(DisableRoutine());
    }

    private IEnumerator DisableRoutine()
    {
        yield return new WaitForSeconds(m_DestroyDelay);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// プレイヤーに到達したときの処理。
    /// </summary>
    private void ReachPlayer()
    {
        if (m_IsHit) return;
        Deactivate();

        GameManager.Instance.OnTargetReachPlayer(this);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 的を無効化する共通処理。被弾時・プレイヤー到達時の両方で呼ばれます。
    /// </summary>
    private void Deactivate()
    {
        m_IsHit = true;
        m_IsMoving = false;
        m_Collider.enabled = false;
        m_TargetRenderer.enabled = false;
    }

    /// <summary>
    /// 指定された位置と移動パターンで的を生成し、移動を開始します。
    /// </summary>
    /// <param name="spawnPos">出現させる座標</param>
    /// <param name="pattern">移動軌道パターン</param>
    public void Spawn(Vector3 spawnPos, TargetMovementPattern pattern)
    {
        StopAllCoroutines(); // 進行中のコルーチンを停止

        transform.position = spawnPos;
        m_SpawnPosition = spawnPos;
        m_ActivePattern = pattern;
        m_LifeTime = 0f;
        m_IsHit = false;
        m_IsMoving = true;

        // ジグザグ・螺旋用のパラメータにランダムな揺らぎを加える
        m_WaveFrequency = Random.Range(2.0f, 4.0f);
        m_WaveAmplitude = Random.Range(0.5f, 1.2f);

        m_TargetRenderer.enabled = true;
        m_Collider.enabled = true;

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 的を初期位置に復活させ、状態をリセットします。
    /// </summary>
    public void ResetTarget()
    {
        StopAllCoroutines(); // 進行中の非アクティブ化コルーチンを停止

        transform.position = m_InitialPosition;
        transform.rotation = m_InitialRotation;
        m_IsHit = false;
        m_IsMoving = false;

        m_TargetRenderer.enabled = true;
        m_Collider.enabled = true;
    }
}
