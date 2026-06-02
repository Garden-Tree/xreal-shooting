using System.Collections;
using UnityEngine;

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

    private Material m_Material;
    private bool m_IsLitThisFrame = false;
    private bool m_IsHit = false;

    private void Awake()
    {
        // レンダラーの自動取得
        if (m_TargetRenderer == null)
        {
            m_TargetRenderer = GetComponent<Renderer>();
        }

        // オーディオソースの自動取得・追加
        if (m_AudioSource == null)
        {
            m_AudioSource = GetComponent<AudioSource>();
            if (m_AudioSource == null)
            {
                m_AudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        // 動的なマテリアルのインスタンス化（他のオブジェクトへの影響を防ぐ）
        if (m_TargetRenderer != null)
        {
            m_Material = m_TargetRenderer.material;
            // 初期状態はEmissionをOFF（黒色）にする
            SetEmission(false);
        }
        else
        {
            Debug.LogWarning($"[{name}] Rendererが設定されていません。マテリアル制御が動作しません。");
        }
    }

    private void Update()
    {
        // すでに破壊処理中の場合は処理しない
        if (m_IsHit) return;

        // 毎フレームのUpdateの開始時（または前フレームのクリア後）にライト判定をリセットする準備
        // 判定はLateUpdateで行うため、ここではフラグの初期化は行わず、
        // 外部（BeamProController）から毎フレームUpdate内で SetLit() が呼ばれることを前提とします。
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

        // 1. 物理衝突判定を無効化（二重ヒットを防ぐ）
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }

        // 2. 的の見た目を非表示にする（オブジェクト自体を非アクティブにするとコルーチンやSEも止まるためRendererのみ無効化）
        if (m_TargetRenderer != null)
        {
            m_TargetRenderer.enabled = false;
        }

        // 3. パーティクルエフェクトの再生
        if (m_HitEffect != null)
        {
            m_HitEffect.transform.parent = null; // 的消滅時にエフェクトが消えないように親子関係を解除
            m_HitEffect.Play();
            Destroy(m_HitEffect.gameObject, m_DestroyDelay); // 再生後にパーティクルオブジェクトも破棄
        }

        // 4. 効果音の再生
        if (m_AudioSource != null && m_HitSound != null)
        {
            m_AudioSource.PlayOneShot(m_HitSound);
        }

        // 5. 指定秒数後にこのオブジェクトを完全に削除
        Destroy(gameObject, m_DestroyDelay);
    }

    /// <summary>
    /// マテリアルのEmission（発光）状態を設定します。
    /// </summary>
    /// <param name="enabled">発光を有効にするかどうか</param>
    private void SetEmission(bool enabled)
    {
        if (m_Material == null) return;

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
