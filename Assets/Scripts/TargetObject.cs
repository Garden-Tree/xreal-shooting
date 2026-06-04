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
/// 通常時は透明（黒色）になり、フラッシュライトが当たっている間だけ発光（Emission）します。
/// 被弾時には、SEとパーティクルを再生した後に自身を破棄します。
/// </summary>
[RequireComponent(typeof(Collider))]
public class TargetObject : MonoBehaviour
{
    [Header("マテリアル設定")]
    [Tooltip("発光制御を行うレンダラー。未設定の場合は自動で取得します。")]
    [SerializeField]
    private Renderer m_TargetRenderer;

    [Tooltip("ライト照射時の発光色（Emission）")]
    [SerializeField]
    [ColorUsage(true, true)]
    private Color m_EmissionColor = Color.green;

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

    [Tooltip("被弾してからオブジェクトを完全に削除するまでのディレイ秒数")]
    [SerializeField]
    private float m_DestroyDelay = 2.0f;

    [Header("移動設定")]
    [Tooltip("的の移動速度")]
    [SerializeField]
    private float m_MoveSpeed = 2.0f;

    private Material m_Material;
    private bool m_IsLitThisFrame = false;
    private bool m_IsHit = false;

    private Vector3 m_InitialPosition;
    private Quaternion m_InitialRotation;

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

    private bool m_HasSavedInitialTransform = false;

    private void Start()
    {
        SaveInitialTransform();
    }

    private void EnsureInitialized()
    {
        if (m_TargetRenderer == null) m_TargetRenderer = GetComponent<Renderer>();
        if (m_AudioSource == null) m_AudioSource = gameObject.AddComponent<AudioSource>();
        if (m_Material == null && m_TargetRenderer != null) m_Material = m_TargetRenderer.material;
    }

    private void Awake()
    {
        EnsureInitialized();
        SaveInitialTransform();
        SetEmission(false);
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

    private void LateUpdate()
    {
        if (m_IsHit) return;

        // このフレーム中にライトが当たったかどうかに基づいてマテリアルの発光を切り替える
        SetEmission(m_IsLitThisFrame);

        // 次フレームのためにフラグをリセット
        m_IsLitThisFrame = false;
    }

    /// <summary>
    /// 外部（コントローラー）から呼び出され、このフレームでライトが当たっていることを示します。
    /// </summary>
    public void SetLit()
    {
        m_IsLitThisFrame = true;
    }

    /// <summary>
    /// 被弾（射撃命中）したときに呼び出されます。
    /// </summary>
    public void OnHit()
    {
        if (m_IsHit) return; // 二重被弾防止
        m_IsHit = true;
        m_IsMoving = false; // 移動を停止

        GameManager.Instance.AddScore(100);

        GetComponent<Collider>().enabled = false;
        m_TargetRenderer.enabled = false;

        // 3. パーティクルエフェクトの再生（復活させて再利用できるように複製を作成して実行）
        if (m_HitEffect != null)
        {
            ParticleSystem effectInstance = Instantiate(m_HitEffect, transform.position, transform.rotation);
            effectInstance.gameObject.SetActive(true);
            effectInstance.Play();
            Destroy(effectInstance.gameObject, m_DestroyDelay); // 再生後にクローンを破棄
        }

        // 4. 効果音の再生
        if (m_HitSound != null) m_AudioSource.PlayOneShot(m_HitSound);

        // 5. 指定秒数後にこのオブジェクトを非アクティブ化 (Destroyはせず再利用)
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
        m_IsHit = true;
        m_IsMoving = false; // 移動を停止

        GetComponent<Collider>().enabled = false;
        m_TargetRenderer.enabled = false;
        GameManager.Instance.OnTargetReachPlayer(this);

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 指定された位置と移動パターンで的を生成し、移動を開始します。
    /// </summary>
    /// <param name="spawnPos">出現させる座標</param>
    /// <param name="pattern">移動軌道パターン</param>
    public void Spawn(Vector3 spawnPos, TargetMovementPattern pattern)
    {
        EnsureInitialized();
        StopAllCoroutines(); // 進行中のコルーチンを停止
        SaveInitialTransform(); // 念のため、初期位置が記憶されていない場合はここで記憶

        transform.position = spawnPos;
        m_SpawnPosition = spawnPos;
        m_ActivePattern = pattern;
        m_LifeTime = 0f;
        m_IsHit = false;
        m_IsLitThisFrame = false;
        m_IsMoving = true; // 移動を開始

        // ジグザグ・螺旋用のパラメータにランダムな揺らぎを加える
        m_WaveFrequency = Random.Range(2.0f, 4.0f);
        m_WaveAmplitude = Random.Range(0.5f, 1.2f);

        m_TargetRenderer.enabled = true;
        SetEmission(false);
        GetComponent<Collider>().enabled = true;

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 的を初期位置に復活させ、状態をリセットします。
    /// </summary>
    public void ResetTarget()
    {
        EnsureInitialized();
        StopAllCoroutines(); // 進行中の非アクティブ化コルーチンを停止
        
        SaveInitialTransform(); // 念のため、初期位置が記憶されていない場合はここで記憶
        
        transform.position = m_InitialPosition;
        transform.rotation = m_InitialRotation;
        m_IsHit = false;
        m_IsLitThisFrame = false;
        m_IsMoving = false; // 移動状態をクリア
        
        m_TargetRenderer.enabled = true;
        SetEmission(false);
        GetComponent<Collider>().enabled = true;
    }

    /// <summary>
    /// マテリアルのEmission（発光）状態を設定します。
    /// </summary>
    /// <param name="enabled">発光を有効にするかどうか</param>
    private void SetEmission(bool enabled)
    {
        if (enabled)
        {
            m_Material.EnableKeyword("_EMISSION");
            m_Material.SetColor("_EmissionColor", m_EmissionColor);
        }
        else
        {
            m_Material.SetColor("_EmissionColor", Color.black);
            m_Material.DisableKeyword("_EMISSION");
        }
    }

    private void OnDestroy()
    {
        // 生成した動的マテリアルのメモリリーク防止
        if (m_Material != null)
        {
            Destroy(m_Material);
        }
    }
}
