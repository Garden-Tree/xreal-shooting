using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Beam Proコントローラーのポインターとして動作するスクリプト。
    /// 常にフラッシュライトのようにRaycastを飛ばし、的に当たるとその的を発光させます。
    /// 画面タップ（トリガー入力）時に、手ブレ補正を伴う太い射撃判定（SphereCast）を行い、命中した的を破壊します。
    /// </summary>
    public class BeamProController : MonoBehaviour
    {
        [Header("ポインター設定")]
        [Tooltip("コントローラーのポインター（光線）の起点となるTransform。空の場合、実行時に自動で探索します。")]
        [SerializeField]
        private Transform m_PointerTransform;

        [Tooltip("索敵/射撃の対象とするレイヤー（通常は的オブジェクトを設定したレイヤーを指定します）")]
        [SerializeField]
        private LayerMask m_TargetLayerMask = 1;

        [Header("索敵（フラッシュライト）設定")]
        [Tooltip("フラッシュライトが届く最大距離")]
        [SerializeField]
        private float m_MaxDiscoveryDistance = 15f;

        [Header("射撃（エイムアシスト）設定")]
        [Tooltip("射撃の最大射程距離")]
        [SerializeField]
        private float m_MaxShootDistance = 20f;

        [Tooltip("エイムアシストの太さ（SphereCastの半径）。値が大きいほど当たりやすくなります。")]
        [SerializeField]
        private float m_AimAssistRadius = 0.5f;

        [Header("入力アクション設定")]
        [Tooltip("射撃ボタン（トリガー）の入力アクション")]
        [SerializeField]
        private InputActionProperty m_TriggerAction;

        private InputAction m_DynamicTriggerAction;

        [Header("フラッシュライト可視化設定")]
        [Tooltip("ライトコーン（光線）の描画に LineRenderer を使用するかどうか")]
        [SerializeField]
        private bool m_UseLightBeamVisual = true;

        [Tooltip("ライトコーンの開始幅（コントローラー先端付近）")]
        [SerializeField]
        private float m_LightBeamWidthStart = 0.05f;

        [Tooltip("ライトコーンの終了幅（最遠地点またはヒット地点付近）")]
        [SerializeField]
        private float m_LightBeamWidthEnd = 1.2f;

        [Tooltip("ライトコーン用マテリアル。未設定の場合は半透明の薄い黄色マテリアルが動的に作成されます。")]
        [SerializeField]
        private Material m_LightBeamMaterial;

        [Tooltip("物理スポットライトの強さ")]
        [SerializeField]
        private float m_SpotLightIntensity = 8f;

        [Tooltip("物理スポットライトの角度（度）")]
        [SerializeField]
        private float m_SpotLightAngle = 25f;

        [Tooltip("照射位置（ヒットポイント）に表示するレティクル（光の輪）のプレハブ")]
        [SerializeField]
        private GameObject m_LightReticlePrefab;

        private LineRenderer m_LineRenderer;
        private Light m_SpotLight;
        private GameObject m_ReticleInstance;
        private Material m_DynamicBeamMaterial;
        private Material m_DynamicReticleMaterial;

        [Header("バイブレーション設定")]
        [Tooltip("射撃時のバイブレーションの強度 (0.0 ～ 1.0)")]
        [SerializeField, Range(0f, 1f)]
        private float m_ShootVibrationAmplitude = 0.3f;
        [Tooltip("射撃時のバイブレーションの持続時間（秒）")]
        [SerializeField]
        private float m_ShootVibrationDuration = 0.08f;

        [Tooltip("命中時のバイブレーションの強度 (0.0 ～ 1.0)")]
        [SerializeField, Range(0f, 1f)]
        private float m_HitVibrationAmplitude = 0.8f;
        [Tooltip("命中時のバイブレーションの持続時間（秒）")]
        [SerializeField]
        private float m_HitVibrationDuration = 0.2f;

        [Header("効果音設定")]
        [Tooltip("射撃音などを鳴らすオーディオソース。未設定の場合は自動取得または追加されます。")]
        [SerializeField]
        private AudioSource m_AudioSource;

        [Tooltip("射撃音のSEクリップ")]
        [SerializeField]
        private AudioClip m_ShootSound;

        private void Awake()
        {
            // オーディオソースの自動設定
            if (m_AudioSource == null)
            {
                m_AudioSource = GetComponent<AudioSource>();
                if (m_AudioSource == null)
                {
                    m_AudioSource = gameObject.AddComponent<AudioSource>();
                }
            }
        }

        private IEnumerator Start()
        {
            // 起動時に HMD を 6DoF モードに強制設定する
            if (XREALPlugin.GetTrackingType() != TrackingType.MODE_6DOF)
            {
                Debug.Log("[XREAL] HMDのトラッキングを6DoFモードに切り替えます...");
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_6DOF, (result, targetMode) => {
                    Debug.Log($"[XREAL] 6DoF切り替え結果: {result}, 現在のモード: {XREALPlugin.GetTrackingType()}");
                });
            }

            // ポインターTransformが未設定の場合、XREAL SDKの標準的なコントローラー階層から探索
            if (m_PointerTransform == null)
            {
                float timer = 0f;
                while (timer < 5.0f && m_PointerTransform == null)
                {
                    yield return new WaitForSeconds(0.5f);
                    timer += 0.5f;

                    GameObject controller = GameObject.Find("Right Controller");
                    if (controller != null)
                    {
                        // Ray Interactor または Near-Far Interactor をポインターの起点として使用
                        m_PointerTransform = controller.transform.Find("Ray Interactor");
                        if (m_PointerTransform == null)
                        {
                            m_PointerTransform = controller.transform.Find("Near-Far Interactor");
                        }
                        if (m_PointerTransform == null)
                        {
                            m_PointerTransform = controller.transform;
                        }
                    }
                }
            }

            // それでも見つからない場合は、アタッチ先自身を起点とする
            if (m_PointerTransform == null)
            {
                m_PointerTransform = transform;
                Debug.LogWarning($"[{name}] ポインター起点となるTransformが自動検出されなかったため、自身({name})のTransformを起点として設定しました。");
            }

            // フラッシュライト関連のビジュアル初期化
            InitializeFlashlightVisuals();

            // InputActionの有効化
            if (m_TriggerAction.action != null)
            {
                m_TriggerAction.action.Enable();
            }
            else
            {
                // インスペクターで設定されていない場合、XREAL Actions.inputactions で定義されている
                // バインディング名（<XREALController>/TriggerButton, <XRSimulatedController>/triggerButton）を動的に登録
                m_DynamicTriggerAction = new InputAction("XREALTrigger", binding: "<XREALController>/TriggerButton");
                m_DynamicTriggerAction.AddBinding("<XRSimulatedController>/triggerButton");
                m_DynamicTriggerAction.Enable();
            }
        }

        private void OnDisable()
        {
            if (m_DynamicTriggerAction != null)
            {
                m_DynamicTriggerAction.Disable();
                m_DynamicTriggerAction.Dispose();
                m_DynamicTriggerAction = null;
            }
        }

        private void Update()
        {
            if (m_PointerTransform == null) return;

            // 1. 索敵機能: 常に細いRaycastを飛ばし、ヒットした的を発光させる
            PerformFlashlightDiscovery();

            // 2. 射撃判定: 画面タップ（トリガー入力）の検知
            if (IsTriggerPressedThisFrame())
            {
                Shoot();
            }
        }

        /// <summary>
        /// コントローラーからRaycastを飛ばし、ヒットしたTargetObjectを発光（SetLit）させます。
        /// 同時に、スポットライトビームと照射位置のレティクル（光の輪）を更新します。
        /// </summary>
        private void PerformFlashlightDiscovery()
        {
            Ray ray = new Ray(m_PointerTransform.position, m_PointerTransform.forward);
            RaycastHit hit;
            bool didHit = Physics.Raycast(ray, out hit, m_MaxDiscoveryDistance, m_TargetLayerMask);

            float targetDistance = m_MaxDiscoveryDistance;

            if (didHit)
            {
                targetDistance = hit.distance;
                TargetObject target = hit.collider.GetComponent<TargetObject>();
                if (target != null)
                {
                    target.SetLit();
                }

                // 照射位置にレティクル（光の輪）を投影
                if (m_ReticleInstance != null)
                {
                    m_ReticleInstance.SetActive(true);
                    m_ReticleInstance.transform.position = hit.point + hit.normal * 0.02f; // 壁にめり込まないように少し浮かせる
                    m_ReticleInstance.transform.rotation = Quaternion.LookRotation(-hit.normal); // 面の法線方向に向ける
                    
                    // 距離に応じたサイズスケーリング（円錐光の広がりをシミュレート）
                    float scale = Mathf.Lerp(m_LightBeamWidthStart, m_LightBeamWidthEnd, hit.distance / m_MaxDiscoveryDistance);
                    m_ReticleInstance.transform.localScale = Vector3.one * scale;
                }
            }
            else
            {
                if (m_ReticleInstance != null)
                {
                    m_ReticleInstance.SetActive(false);
                }
            }

            // LineRendererでライトビーム（光線）を描画
            if (m_UseLightBeamVisual && m_LineRenderer != null)
            {
                m_LineRenderer.SetPosition(0, m_PointerTransform.position);
                m_LineRenderer.SetPosition(1, m_PointerTransform.position + m_PointerTransform.forward * targetDistance);

                // 照射距離に合わせて先端の太さをスケーリング
                float dynamicEndWidth = Mathf.Lerp(m_LightBeamWidthStart, m_LightBeamWidthEnd, targetDistance / m_MaxDiscoveryDistance);
                m_LineRenderer.endWidth = dynamicEndWidth;
            }

            // デバッグ表示用（エディタのSceneビューで緑色のレーザーラインを表示）
            Debug.DrawRay(m_PointerTransform.position, m_PointerTransform.forward * m_MaxDiscoveryDistance, Color.green);
        }

        /// <summary>
        /// エイムアシスト付きの射撃処理を行います。
        /// </summary>
        private void Shoot()
        {
            // 1. 射撃時のバイブレーションフィードバック
            TriggerHaptic(m_ShootVibrationAmplitude, m_ShootVibrationDuration);

            // 2. 射撃音の再生
            if (m_AudioSource != null && m_ShootSound != null)
            {
                m_AudioSource.PlayOneShot(m_ShootSound);
            }

            // 3. エイムアシスト機能 (SphereCast)
            Ray ray = new Ray(m_PointerTransform.position, m_PointerTransform.forward);
            RaycastHit hit;

            // 太いレーザー（SphereCast）を飛ばして手ブレを補正
            if (Physics.SphereCast(ray, m_AimAssistRadius, out hit, m_MaxShootDistance, m_TargetLayerMask))
            {
                TargetObject target = hit.collider.GetComponent<TargetObject>();
                if (target != null)
                {
                    // ターゲットのヒット時演出と自己破棄を実行
                    target.OnHit();

                    // 4. 命中時のバイブレーションフィードバック（より強く、長く）
                    TriggerHaptic(m_HitVibrationAmplitude, m_HitVibrationDuration);
                }
            }
        }

        /// <summary>
        /// トリガー入力（画面タップ）がこのフレームで行われたかを判定します。
        /// </summary>
        private bool IsTriggerPressedThisFrame()
        {
            // (A) インスペクターから割り当てられた InputActionProperty による検知
            if (m_TriggerAction.action != null && m_TriggerAction.action.WasPressedThisFrame())
            {
                return true;
            }

            // (A-2) 動的に生成された InputSystem アクションによる検知
            if (m_DynamicTriggerAction != null && m_DynamicTriggerAction.WasPressedThisFrame())
            {
                return true;
            }

            // (B) 既存の XREALInput が有効な場合のフォールバック（LegacyTools対応）
            try
            {
                if (XREALInput.GetButtonDown(ControllerButton.TRIGGER))
                {
                    return true;
                }
            }
            catch
            {
                // XREALInputが存在しない、または例外発生時はスキップ
            }

            // (C) エディタ内テストおよび画面タッチ用のマウスボタン・タップ検知
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// XREAL SDK準拠のバイブレーションを送信します。
        /// </summary>
        /// <param name="amplitude">強度 (0.0 ～ 1.0)</param>
        /// <param name="duration">持続時間（秒）</param>
        private void TriggerHaptic(float amplitude, float duration)
        {
            // HelloMR サンプルと同様に XREALVirtualController.Singleton から呼び出し
            if (XREALVirtualController.Singleton != null && XREALVirtualController.Singleton.Controller != null)
            {
                XREALVirtualController.Singleton.Controller.SendHapticImpulse(0, amplitude, duration);
            }
            else
            {
                // フォールバック: XREALInputのハプティクス呼び出し
                try
                {
                    XREALInput.TriggerHapticVibration(duration, amplitude);
                }
                catch
                {
                    // SDKが動作していない（エディタ再生時など）はスキップ
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            // インスペクターで選択中、Sceneビューでエイムアシストの太さ（SphereCastの範囲）を可視化
            if (m_PointerTransform == null) return;

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Vector3 origin = m_PointerTransform.position;
            Vector3 endPoint = origin + m_PointerTransform.forward * m_MaxShootDistance;

            // 始点と終点の球体
            Gizmos.DrawWireSphere(origin, m_AimAssistRadius);
            Gizmos.DrawWireSphere(endPoint, m_AimAssistRadius);

            // 始点から終点への境界線を描画
            Vector3 up = m_PointerTransform.up * m_AimAssistRadius;
            Vector3 right = m_PointerTransform.right * m_AimAssistRadius;

            Gizmos.DrawLine(origin + up, endPoint + up);
            Gizmos.DrawLine(origin - up, endPoint - up);
            Gizmos.DrawLine(origin + right, endPoint + right);
            Gizmos.DrawLine(origin - right, endPoint - right);
        }

        /// <summary>
        /// フラッシュライトの物理ライト、ライトコーン（光線）、およびレティクルを初期化します。
        /// </summary>
        private void InitializeFlashlightVisuals()
        {
            if (m_PointerTransform == null) return;

            // 1. スポットライトコンポーネントの追加・設定
            m_SpotLight = m_PointerTransform.GetComponent<Light>();
            if (m_SpotLight == null)
            {
                m_SpotLight = m_PointerTransform.gameObject.AddComponent<Light>();
            }
            m_SpotLight.type = LightType.Spot;
            m_SpotLight.range = m_MaxDiscoveryDistance;
            m_SpotLight.spotAngle = m_SpotLightAngle;
            m_SpotLight.intensity = m_SpotLightIntensity;
            m_SpotLight.color = new Color(1f, 0.95f, 0.8f); // 暖かみのある白

            // 2. LineRenderer（ライトビーム）の追加・設定
            if (m_UseLightBeamVisual)
            {
                m_LineRenderer = m_PointerTransform.GetComponent<LineRenderer>();
                if (m_LineRenderer == null)
                {
                    m_LineRenderer = m_PointerTransform.gameObject.AddComponent<LineRenderer>();
                }
                m_LineRenderer.positionCount = 2;
                
                if (m_LightBeamMaterial == null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        m_DynamicBeamMaterial = new Material(shader);
                        // 半透明の薄い黄色
                        m_DynamicBeamMaterial.color = new Color(1f, 0.9f, 0.5f, 0.12f);
                        m_LightBeamMaterial = m_DynamicBeamMaterial;
                    }
                }
                
                if (m_LightBeamMaterial != null)
                {
                    m_LineRenderer.material = m_LightBeamMaterial;
                }
                m_LineRenderer.startWidth = m_LightBeamWidthStart;
                m_LineRenderer.endWidth = m_LightBeamWidthEnd;

                // フェードアウトするカラーグラデーション
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(new Color(1f, 0.9f, 0.5f), 1.0f) },
                    new GradientAlphaKey[] { new GradientAlphaKey(0.15f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
                );
                m_LineRenderer.colorGradient = gradient;
            }

            // 3. 照射位置表示用レティクルの初期化
            if (m_LightReticlePrefab != null)
            {
                m_ReticleInstance = Instantiate(m_LightReticlePrefab);
            }
            else
            {
                // レティクルプレハブが未指定の場合、簡易的な円（クアッド）を自動生成
                m_ReticleInstance = GameObject.CreatePrimitive(PrimitiveType.Quad);
                m_ReticleInstance.name = "FlashlightReticle";
                
                Collider col = m_ReticleInstance.GetComponent<Collider>();
                if (col != null) Destroy(col);
                
                Renderer rend = m_ReticleInstance.GetComponent<Renderer>();
                if (rend != null)
                {
                    Shader shader = Shader.Find("Sprites/Default");
                    if (shader != null)
                    {
                        m_DynamicReticleMaterial = new Material(shader);
                        // レティクルカラー（光の輪）
                        m_DynamicReticleMaterial.color = new Color(1f, 0.95f, 0.6f, 0.3f);
                        rend.material = m_DynamicReticleMaterial;
                    }
                }
                m_ReticleInstance.transform.localScale = Vector3.one * m_LightBeamWidthEnd;
            }
            m_ReticleInstance.SetActive(false);
        }

        private void OnDestroy()
        {
            // 動的マテリアルの破棄（メモリリーク防止）
            if (m_DynamicBeamMaterial != null)
            {
                Destroy(m_DynamicBeamMaterial);
            }
            if (m_DynamicReticleMaterial != null)
            {
                Destroy(m_DynamicReticleMaterial);
            }
            if (m_ReticleInstance != null)
            {
                Destroy(m_ReticleInstance);
            }
        }
    }
}
