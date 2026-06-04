using System.Collections;
using System.Collections.Generic;
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

        private XREALActions m_XREALActions;

        [Header("フラッシュライト可視化設定 (ボリュメトリック風)")]
        [Tooltip("ライトコーン（光線）の描画にカスタムメッシュを使用するかどうか")]
        [SerializeField]
        private bool m_UseLightBeamVisual = true;

        [Tooltip("ライトコーンの開始幅（コントローラー先端付近）")]
        [SerializeField]
        private float m_LightBeamWidthStart = 0.05f;

        [Tooltip("ライトコーンの終了幅（最遠地点またはヒット地点付近）")]
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

        // --- カスタムコーンメッシュ関連 ---
        private MeshFilter m_BeamMeshFilter;
        private MeshRenderer m_BeamMeshRenderer;
        private Mesh m_BeamMesh;
        private Vector3[] m_BeamVertices;
        private Color32[] m_BeamColors;
        private int[] m_BeamIndices;

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

            // 1. 索敵機能: 常に細いRaycastを飛ばし、ヒットした的を発光させる
            PerformFlashlightDiscovery();

            // 2. 射撃判定: 画面タップ（トリガー入力）の検知
            if (IsTriggerPressedThisFrame())
            {
                Shoot();
            }

            // 3. Appボタン入力による手動キャリブレーション（リセット）の実行
            if (m_XREALActions.XREALButtons.App.WasPressedThisFrame())
            {
                GameManager.Instance?.ResetGame();
            }
        }

        /// <summary>
        /// ボリュメトリックライトコーンメッシュの更新と、物理スポットライトの角度同期を行います。
        /// </summary>
        private void PerformFlashlightDiscovery()
        {
            // カスタムメッシュでボリュメトリックライトコーンを描画 (常に一定の長さ)
            if (m_UseLightBeamVisual && m_BeamMesh != null)
            {
                UpdateBeamMesh(m_MaxDiscoveryDistance);
            }

            // SpotLightの角度も常にLightBeamWidthEndとMaxDiscoveryDistanceから同期させる
            if (m_SpotLight != null)
            {
                float halfAngle = Mathf.Atan2(m_LightBeamWidthEnd, m_MaxDiscoveryDistance) * Mathf.Rad2Deg;
                m_SpotLight.spotAngle = halfAngle * 2f;
            }

            Debug.DrawRay(m_PointerTransform.position, m_PointerTransform.forward * m_MaxDiscoveryDistance, Color.green);
        }

        /// <summary>
        /// ボリュメトリックライト風のコーンメッシュを生成・更新します。
        /// </summary>
        private void UpdateBeamMesh(float distance)
        {
            float dynamicEndWidth = Mathf.Lerp(m_LightBeamWidthStart, m_LightBeamWidthEnd, distance / m_MaxDiscoveryDistance);
            
            // 頂点の更新 (ローカル座標系)
            // 0: Start Center
            // 1: End Center
            // 2 ~ : Start Outer Rings
            // 2+Segments ~ : End Outer Rings

            m_BeamVertices[0] = Vector3.zero;
            m_BeamVertices[1] = Vector3.forward * distance;

            for (int i = 0; i < m_BeamSegments; i++)
            {
                float angle = (i / (float)m_BeamSegments) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle);
                float y = Mathf.Sin(angle);

                // Start Outer
                m_BeamVertices[i + 2] = new Vector3(x * m_LightBeamWidthStart, y * m_LightBeamWidthStart, 0f);
                // End Outer
                m_BeamVertices[i + 2 + m_BeamSegments] = new Vector3(x * dynamicEndWidth, y * dynamicEndWidth, distance);
            }

            m_BeamMesh.vertices = m_BeamVertices;
            
            // Boundsを更新してカリングされないようにする
            m_BeamMesh.RecalculateBounds();
        }

        private void Shoot()
        {
            TriggerHaptic(m_ShootVibrationAmplitude, m_ShootVibrationDuration);

            if (m_ShootSound != null) m_AudioSource.PlayOneShot(m_ShootSound);

            Ray ray = new Ray(m_PointerTransform.position, m_PointerTransform.forward);
            RaycastHit hit;

            if (Physics.SphereCast(ray, m_AimAssistRadius, out hit, m_MaxShootDistance, m_TargetLayerMask))
            {
                if (hit.collider.TryGetComponent<TargetObject>(out var target))
                {
                    target.OnHit();
                    TriggerHaptic(m_HitVibrationAmplitude, m_HitVibrationDuration);
                }
            }
        }

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

                m_BeamMeshFilter = beamObj.AddComponent<MeshFilter>();
                m_BeamMeshRenderer = beamObj.AddComponent<MeshRenderer>();
                m_BeamMesh = new Mesh();
                m_BeamMesh.name = "VolumetricBeamMesh";
                m_BeamMesh.MarkDynamic();

                InitializeBeamMeshStruct();
                m_BeamMeshFilter.mesh = m_BeamMesh;

                if (m_LightBeamMaterial == null)
                {
                    // URP環境でも確実に「頂点カラーによるグラデーション」と「半透明」が動作する
                    // Sprites/Default シェーダーを基本として使用します。
                    // （Sprites/Defaultは標準でZWrite Off, Alpha Blend, Cull Offとなっており安全です）
                    Shader shader = Shader.Find("Sprites/Default");

                    m_DynamicBeamMaterial = new Material(shader);
                    m_LightBeamMaterial = m_DynamicBeamMaterial;
                }
                
                m_BeamMeshRenderer.material = m_LightBeamMaterial;
            }
        }

        private void InitializeBeamMeshStruct()
        {
            int vertexCount = 2 + m_BeamSegments * 2;
            m_BeamVertices = new Vector3[vertexCount];
            m_BeamColors = new Color32[vertexCount];
            
            // 頂点カラーの設定 (アルファフェード)
            Color32 startCenterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0f * m_LightColor.a); // 根本中心を完全に透明に
            Color32 endCenterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0.5f * m_LightColor.a); // 底面中心
            Color32 startOuterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0f * m_LightColor.a); // 根本の側面を完全に透明に
            Color32 endOuterColor = new Color(m_LightColor.r, m_LightColor.g, m_LightColor.b, 0.05f * m_LightColor.a); // 先端の側面（非常に薄く）

            m_BeamColors[0] = startCenterColor; // Start Center
            m_BeamColors[1] = endCenterColor; // End Center (底面)

            for (int i = 0; i < m_BeamSegments; i++)
            {
                m_BeamColors[i + 2] = startOuterColor; // Start Outer Rings
                m_BeamColors[i + 2 + m_BeamSegments] = endOuterColor; // End Outer Rings
            }

            // インデックス配列の生成
            // 1セグメントあたり: 
            // 始点キャップ(手前): 3
            // 側面: 6
            // 終点キャップ(底面): 3
            int trianglesPerSegment = 3 + 6 + 3;
            m_BeamIndices = new int[m_BeamSegments * trianglesPerSegment];

            int idx = 0;
            for (int i = 0; i < m_BeamSegments; i++)
            {
                int currentOuter = i + 2;
                int nextOuter = ((i + 1) % m_BeamSegments) + 2;
                int currentEnd = currentOuter + m_BeamSegments;
                int nextEnd = nextOuter + m_BeamSegments;

                // Start Cap (手前の面 - 時計回り)
                m_BeamIndices[idx++] = 0;
                m_BeamIndices[idx++] = currentOuter;
                m_BeamIndices[idx++] = nextOuter;

                // Side (側面 - 外側に向けて描画)
                m_BeamIndices[idx++] = currentOuter;
                m_BeamIndices[idx++] = currentEnd;
                m_BeamIndices[idx++] = nextOuter;

                m_BeamIndices[idx++] = nextOuter;
                m_BeamIndices[idx++] = currentEnd;
                m_BeamIndices[idx++] = nextEnd;

                // End Cap (奥の底面 - 反時計回り)
                m_BeamIndices[idx++] = 1;
                m_BeamIndices[idx++] = nextEnd;
                m_BeamIndices[idx++] = currentEnd;
            }

            m_BeamMesh.vertices = m_BeamVertices;
            m_BeamMesh.colors32 = m_BeamColors;
            m_BeamMesh.triangles = m_BeamIndices;
        }

        private void OnDestroy()
        {
            if (m_DynamicBeamMaterial != null) Destroy(m_DynamicBeamMaterial);
            if (m_BeamMesh != null) Destroy(m_BeamMesh);
        }
    }
}
