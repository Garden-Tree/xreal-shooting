using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// Beam Proコントローラーのポインターとして動作するスクリプト。
    /// 常にフラッシュライトのようなボリュメトリックコーンを描画します。
    /// 画面タップ（トリガー入力）時に、手ブレ補正を伴う太い射撃判定（SphereCast）を行い、命中した的を破壊します。
    /// </summary>
    public class BeamProController : MonoBehaviour
    {
        [Header("ポインター設定")]
        [Tooltip("コントローラーのポインター（光線）の起点となるTransform。空の場合、実行時に自動で探索します。")]
        [SerializeField]
        private Transform m_PointerTransform;

        [Tooltip("射撃の対象とするレイヤー（通常は的オブジェクトを設定したレイヤーを指定します）")]
        [SerializeField]
        private LayerMask m_TargetLayerMask = 1;

        [Header("フラッシュライト設定")]
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

        [Tooltip("射撃のクールタイム（秒）")]
        [SerializeField]
        private float m_ShootCooldown = 0.5f;

        private float m_LastShootTime = -100f;
        private GameState m_PrevState = GameState.Calibration;

        private XREALActions m_XREALActions;

        [Header("フラッシュライト可視化設定 (ボリュメトリック風)")]
        [Tooltip("ライトコーン（光線）の描画にカスタムメッシュを使用するかどうか")]
        [SerializeField]
        private bool m_UseLightBeamVisual = true;

        [Tooltip("ライトコーンの開始幅（コントローラー先端付近）")]
        [SerializeField]
        private float m_LightBeamWidthStart = 0.05f;

        [Tooltip("ライトコーンの終了幅（最遠地点付近）")]
        [SerializeField]
        private float m_LightBeamWidthEnd = 1.2f;

        [Tooltip("ライトコーン用マテリアル。未設定の場合は加算合成の動的マテリアルを作成します。")]
        [SerializeField]
        private Material m_LightBeamMaterial;

        [Tooltip("ライトコーンの基本カラー")]
        [SerializeField]
        private Color m_LightColor = new Color(1f, 0.9f, 0.6f, 1f);

        [Tooltip("メッシュの分割数（円の滑らかさ）")]
        [SerializeField]
        private int m_BeamSegments = 16;

        [Tooltip("物理スポットライトの強さ")]
        [SerializeField]
        private float m_SpotLightIntensity = 8f;

        private Light m_SpotLight;
        private Material m_DynamicBeamMaterial;
        private Mesh m_BeamMesh;

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

        [Tooltip("クールタイム中に発射しようとしたときのエラーSE")]
        [SerializeField]
        private AudioClip m_CannotShootSound;

        [Header("UI設定")]
        [Tooltip("クールタイムを表示する円形ゲージのImage（Image TypeをFilledに設定してください）")]
        [SerializeField]
        private Image m_CooldownGaugeImage;

        [Header("レティクル（着弾予想位置）設定")]
        [Tooltip("着弾予想位置にドットを表示するかどうか")]
        [SerializeField]
        private bool m_ShowReticle = true;

        [Tooltip("着弾予想位置に表示するレティクルのプレハブ。未設定の場合は簡易的な球体を自動生成します。")]
        [SerializeField]
        private GameObject m_ReticlePrefab;

        [Tooltip("自動生成するレティクルのサイズ（直径）")]
        [SerializeField]
        private float m_ReticleSize = 0.05f;

        [Tooltip("レティクルのカラー")]
        [SerializeField]
        private Color m_ReticleColor = new Color(1f, 0.9f, 0.6f, 0.8f);

        private GameObject m_ReticleInstance;
        private Material m_DynamicReticleMaterial;

        private void Awake()
        {
            if (m_AudioSource == null) m_AudioSource = gameObject.AddComponent<AudioSource>();
        }

        private IEnumerator Start()
        {
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
                        m_PointerTransform = controller.transform.Find("Ray Interactor");
                        if (m_PointerTransform == null)
                        {
                            m_PointerTransform = controller.transform.Find("Near-Far Interactor");
                        }
                        if (m_PointerTransform == null)
                        {
                            m_PointerTransform = controller.transform;
                        }
                        Debug.Log($"[{name}] ポインター起点となるTransformを自動検出して設定しました: {m_PointerTransform.name}");
                    }
                }
            }

            // フラッシュライト関連のビジュアル初期化
            InitializeFlashlightVisuals();

            // 着弾予想位置ドット（レティクル）の初期化
            InitializeReticle();

            // 自動生成された XREALActions クラスをインスタンス化して有効化
            m_XREALActions = new XREALActions();
            m_XREALActions.Enable();
        }

        private void OnDisable()
        {
            if (m_XREALActions != null)
            {
                m_XREALActions.Disable();
                m_XREALActions.Dispose();
                m_XREALActions = null;
            }
        }

        private void Update()
        {
            if (m_PointerTransform == null) return;
            if (m_XREALActions == null) return; // Start コルーチン完了前のガード

            // 1. 着弾予想位置ドットの更新
            UpdateReticlePosition();

            GameState currentState = GameManager.Instance != null ? GameManager.Instance.CurrentState : GameState.Calibration;

            // 2. 射撃判定: 画面タップ（トリガー入力）の検知
            // ゲームプレイ中または難易度選択中のみ射撃を許可する
            // （キャリブレーション時のタップで即座に誤射しないよう、状態が変わったフレームでは撃たない）
            if (currentState == GameState.Playing || currentState == GameState.DifficultySelection)
            {
                if (currentState == m_PrevState && IsTriggerPressedThisFrame())
                {
                    TryShoot();
                }
            }

            m_PrevState = currentState;

            // 3. Appボタン入力による手動キャリブレーション（リセット）の実行
            if (m_XREALActions.XREALButtons.App.WasPressedThisFrame())
            {
                GameManager.Instance?.ResetGame();
            }

            // 4. UIゲージの更新
            UpdateCooldownUI();
        }

        #region UI

        private void UpdateCooldownUI()
        {
            if (m_CooldownGaugeImage == null) return;

            float timeSinceLastShoot = Time.time - m_LastShootTime;
            if (timeSinceLastShoot < m_ShootCooldown)
            {
                // クールタイム中：ゲージを表示して 0.0 〜 1.0 の間で増やす
                if (!m_CooldownGaugeImage.enabled) m_CooldownGaugeImage.enabled = true;
                m_CooldownGaugeImage.fillAmount = timeSinceLastShoot / m_ShootCooldown;
            }
            else
            {
                // クールタイム完了（フルチャージ）：ゲージを非表示にする
                if (m_CooldownGaugeImage.enabled) m_CooldownGaugeImage.enabled = false;
            }
        }

        #endregion

        #region 射撃

        private void TryShoot()
        {
            if (Time.time - m_LastShootTime < m_ShootCooldown)
            {
                // クールタイム中
                if (m_CannotShootSound != null) m_AudioSource.PlayOneShot(m_CannotShootSound);
                return;
            }

            // 射撃可能
            m_LastShootTime = Time.time;
            Shoot();
        }

        private void Shoot()
        {
            TriggerHaptic(m_ShootVibrationAmplitude, m_ShootVibrationDuration);

            if (m_ShootSound != null) m_AudioSource.PlayOneShot(m_ShootSound);

            Ray ray = new Ray(m_PointerTransform.position, m_PointerTransform.forward);
            bool isHit = false;

            if (Physics.SphereCast(ray, m_AimAssistRadius, out RaycastHit hit, m_MaxShootDistance, m_TargetLayerMask))
            {
                // TargetObject または DifficultySelectTarget の OnHit() を呼び出す
                if (hit.collider.TryGetComponent<TargetObject>(out var target))
                {
                    target.OnHit();
                    isHit = true;
                }
                else if (hit.collider.TryGetComponent<DifficultySelectTarget>(out var diffTarget))
                {
                    diffTarget.OnHit();
                    isHit = true;
                }

                // 命中時のバイブレーション
                if (isHit)
                {
                    TriggerHaptic(m_HitVibrationAmplitude, m_HitVibrationDuration);
                }
            }

            // どこにも当たらなかった場合はスコアを-10
            if (!isHit)
            {
                GameManager.Instance?.AddScore(-10);
            }
        }

        #endregion

        #region 入力

        private bool IsTriggerPressedThisFrame()
        {
            if (m_XREALActions.XREALButtons.Trigger.WasPressedThisFrame()) return true;
            if (XREALInput.GetButtonDown(ControllerButton.TRIGGER)) return true;
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame) return true;
            return false;
        }

        private void TriggerHaptic(float amplitude, float duration)
        {
            if (XREALVirtualController.Singleton?.Controller != null)
            {
                XREALVirtualController.Singleton.Controller.SendHapticImpulse(0, amplitude, duration);
            }
            else
            {
                XREALInput.TriggerHapticVibration(duration, amplitude);
            }
        }

        #endregion

        #region フラッシュライト（ボリュメトリックコーン）

        private void InitializeFlashlightVisuals()
        {
            if (m_PointerTransform == null) return;

            // 1. スポットライトコンポーネントの追加・設定
            if (!m_PointerTransform.TryGetComponent(out m_SpotLight))
            {
                m_SpotLight = m_PointerTransform.gameObject.AddComponent<Light>();
            }
            m_SpotLight.type = LightType.Spot;
            m_SpotLight.range = m_MaxDiscoveryDistance;

            // LightBeamWidthEnd と MaxDiscoveryDistance から SpotAngle を自動計算
            float halfAngle = Mathf.Atan2(m_LightBeamWidthEnd, m_MaxDiscoveryDistance) * Mathf.Rad2Deg;
            m_SpotLight.spotAngle = halfAngle * 2f;

            m_SpotLight.intensity = m_SpotLightIntensity;
            m_SpotLight.color = new Color(1f, 0.95f, 0.8f);

            // 2. カスタムメッシュ（ボリュメトリックライト風）の追加・設定
            if (m_UseLightBeamVisual)
            {
                GameObject beamObj = new GameObject("LightBeamMesh");
                beamObj.transform.SetParent(m_PointerTransform, false);
                beamObj.transform.localPosition = Vector3.zero;
                beamObj.transform.localRotation = Quaternion.identity;

                var meshFilter = beamObj.AddComponent<MeshFilter>();
                var meshRenderer = beamObj.AddComponent<MeshRenderer>();

                m_BeamMesh = new Mesh();
                m_BeamMesh.name = "VolumetricBeamMesh";

                BuildBeamMesh();
                meshFilter.mesh = m_BeamMesh;

                if (m_LightBeamMaterial == null)
                {
                    // URP環境でも確実に「頂点カラーによるグラデーション」と「半透明」が動作する
                    // Sprites/Default シェーダーを基本として使用します。
                    // （Sprites/Defaultは標準でZWrite Off, Alpha Blend, Cull Offとなっており安全です）
                    Shader shader = Shader.Find("Sprites/Default");

                    m_DynamicBeamMaterial = new Material(shader);
                    m_LightBeamMaterial = m_DynamicBeamMaterial;
                }

                meshRenderer.material = m_LightBeamMaterial;
            }
        }

        /// <summary>
        /// ボリュメトリックライト風のコーンメッシュを一度だけ構築します。
        /// </summary>
        private void BuildBeamMesh()
        {
            float distance = m_MaxDiscoveryDistance;
            float endWidth = m_LightBeamWidthEnd;

            // --- 頂点の構築 ---
            // 0: Start Center
            // 1: End Center
            // 2 ~ 2+Segments-1: Start Outer Ring
            // 2+Segments ~ : End Outer Ring
            int vertexCount = 2 + m_BeamSegments * 2;
            var vertices = new Vector3[vertexCount];
            var colors = new Color32[vertexCount];

            vertices[0] = Vector3.zero;
            vertices[1] = Vector3.forward * distance;

            for (int i = 0; i < m_BeamSegments; i++)
            {
                float angle = (i / (float)m_BeamSegments) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);

                vertices[i + 2] = new Vector3(x * m_LightBeamWidthStart, y * m_LightBeamWidthStart, 0f);
                vertices[i + 2 + m_BeamSegments] = new Vector3(x * endWidth, y * endWidth, distance);
            }

            // --- 頂点カラーの設定 (アルファフェード) ---
            Color32 startCenterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0f);
            Color32 endCenterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0.5f * m_LightColor.a);
            Color32 startOuterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0f);
            Color32 endOuterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0.05f * m_LightColor.a);

            colors[0] = startCenterColor;
            colors[1] = endCenterColor;

            for (int i = 0; i < m_BeamSegments; i++)
            {
                colors[i + 2] = startOuterColor;
                colors[i + 2 + m_BeamSegments] = endOuterColor;
            }

            // --- インデックス配列の生成 ---
            // 1セグメントあたり: 始点キャップ(3) + 側面(6) + 終点キャップ(3) = 12
            int trianglesPerSegment = 12;
            var indices = new int[m_BeamSegments * trianglesPerSegment];

            int idx = 0;
            for (int i = 0; i < m_BeamSegments; i++)
            {
                int currentOuter = i + 2;
                int nextOuter = ((i + 1) % m_BeamSegments) + 2;
                int currentEnd = currentOuter + m_BeamSegments;
                int nextEnd = nextOuter + m_BeamSegments;

                // Start Cap (手前の面 - 時計回り)
                indices[idx++] = 0;
                indices[idx++] = currentOuter;
                indices[idx++] = nextOuter;

                // Side (側面 - 外側に向けて描画)
                indices[idx++] = currentOuter;
                indices[idx++] = currentEnd;
                indices[idx++] = nextOuter;

                indices[idx++] = nextOuter;
                indices[idx++] = currentEnd;
                indices[idx++] = nextEnd;

                // End Cap (奥の底面 - 反時計回り)
                indices[idx++] = 1;
                indices[idx++] = nextEnd;
                indices[idx++] = currentEnd;
            }

            // メッシュにデータを設定
            m_BeamMesh.vertices = vertices;
            m_BeamMesh.colors32 = colors;
            m_BeamMesh.triangles = indices;
            m_BeamMesh.RecalculateBounds();
        }

        #endregion

        #region レティクル（着弾予想位置）

        private void InitializeReticle()
        {
            if (!m_ShowReticle || m_PointerTransform == null) return;

            if (m_ReticlePrefab != null)
            {
                m_ReticleInstance = Instantiate(m_ReticlePrefab);
            }
            else
            {
                // 簡易的な球体を自動生成
                m_ReticleInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                m_ReticleInstance.name = "AutoGeneratedReticle";

                // Raycast等に干渉しないようにコライダーを削除
                if (m_ReticleInstance.TryGetComponent<Collider>(out var col))
                {
                    Destroy(col);
                }

                m_ReticleInstance.transform.localScale = Vector3.one * m_ReticleSize;

                // 半透明マテリアルを作成して設定
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    m_DynamicReticleMaterial = new Material(shader);
                    m_DynamicReticleMaterial.color = m_ReticleColor;
                    if (m_ReticleInstance.TryGetComponent<Renderer>(out var renderer))
                    {
                        renderer.material = m_DynamicReticleMaterial;
                    }
                }
            }

            if (m_ReticleInstance != null)
            {
                m_ReticleInstance.transform.position = m_PointerTransform.position + m_PointerTransform.forward * m_MaxShootDistance;
            }
        }

        private void UpdateReticlePosition()
        {
            if (!m_ShowReticle || m_ReticleInstance == null || m_PointerTransform == null)
            {
                if (m_ReticleInstance != null && m_ReticleInstance.activeSelf)
                {
                    m_ReticleInstance.SetActive(false);
                }
                return;
            }

            if (!m_ReticleInstance.activeSelf)
            {
                m_ReticleInstance.SetActive(true);
            }

            Ray ray = new Ray(m_PointerTransform.position, m_PointerTransform.forward);

            // TargetLayerMaskを用いて光の交点を検出
            if (Physics.Raycast(ray, out RaycastHit hit, m_MaxShootDistance, m_TargetLayerMask))
            {
                m_ReticleInstance.transform.position = hit.point;
            }
            else
            {
                m_ReticleInstance.transform.position = ray.origin + ray.direction * m_MaxShootDistance;
            }
        }

        #endregion

        #region エディタ用

        private void OnDrawGizmosSelected()
        {
            if (m_PointerTransform == null) return;

            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Vector3 origin = m_PointerTransform.position;
            Vector3 endPoint = origin + m_PointerTransform.forward * m_MaxShootDistance;

            Gizmos.DrawWireSphere(origin, m_AimAssistRadius);
            Gizmos.DrawWireSphere(endPoint, m_AimAssistRadius);

            Vector3 up = m_PointerTransform.up * m_AimAssistRadius;
            Vector3 right = m_PointerTransform.right * m_AimAssistRadius;

            Gizmos.DrawLine(origin + up, endPoint + up);
            Gizmos.DrawLine(origin - up, endPoint - up);
            Gizmos.DrawLine(origin + right, endPoint + right);
            Gizmos.DrawLine(origin - right, endPoint - right);
        }

        #endregion

        private void OnDestroy()
        {
            if (m_DynamicBeamMaterial != null) Destroy(m_DynamicBeamMaterial);
            if (m_BeamMesh != null) Destroy(m_BeamMesh);
            if (m_DynamicReticleMaterial != null) Destroy(m_DynamicReticleMaterial);
            if (m_ReticleInstance != null) Destroy(m_ReticleInstance);
        }
    }
}
