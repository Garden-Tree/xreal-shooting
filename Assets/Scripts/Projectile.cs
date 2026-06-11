using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// 射撃時に発射される飛翔体（弾道）を制御するスクリプト。
    /// 毎フレーム前方に進み、スイープ（SphereCast）による正確な衝突判定を行います。
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        private float m_Speed = 30f;
        private float m_MaxDistance = 20f;
        private float m_Radius = 0.5f;
        private LayerMask m_TargetLayerMask;
        private BeamProController m_Controller;

        private bool m_HasHit = false;
        private float m_DistanceTraveled = 0f;

        /// <summary>
        /// 飛翔体のパラメータとビジュアルカラーを初期化します。
        /// </summary>
        public void Initialize(
            BeamProController controller, 
            float speed, 
            float maxDistance, 
            float radius, 
            LayerMask layerMask, 
            Color projectileColor, 
            Gradient trailGradient)
        {
            m_Controller = controller;
            m_Speed = speed;
            m_MaxDistance = maxDistance;
            m_Radius = radius;
            m_TargetLayerMask = layerMask;

            m_HasHit = false;
            m_DistanceTraveled = 0f;

            // --- 弾頭へのカラー適用 ---
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r != null && !(r is TrailRenderer))
                {
                    if (r.material.HasProperty("_Color"))
                    {
                        r.material.color = projectileColor;
                    }
                }
            }

            // --- トレイル（軌跡）へのカラー適用 ---
            var trail = GetComponentInChildren<TrailRenderer>(true);
            if (trail != null)
            {
                // グラデーションがインスペクターで未設定の場合は、弾頭カラーから透明へのフェードを動的に構築
                if (trailGradient == null || trailGradient.colorKeys == null || trailGradient.colorKeys.Length == 0)
                {
                    trailGradient = new Gradient();
                    
                    var colorKeys = new GradientColorKey[] {
                        new GradientColorKey(projectileColor, 0f),
                        new GradientColorKey(projectileColor, 1f)
                    };
                    
                    var alphaKeys = new GradientAlphaKey[] {
                        new GradientAlphaKey(projectileColor.a, 0f),
                        new GradientAlphaKey(0f, 1f)
                    };
                    
                    trailGradient.SetKeys(colorKeys, alphaKeys);
                }
                
                trail.colorGradient = trailGradient;
            }
        }

        private void Update()
        {
            if (m_HasHit) return;

            float movementThisFrame = m_Speed * Time.deltaTime;
            Vector3 direction = transform.forward;
            Vector3 nextPosition = transform.position + direction * movementThisFrame;

            // 前フレームから現フレームの移動範囲で SphereCast を行い、すり抜けを防止
            Ray ray = new Ray(transform.position, direction);
            if (Physics.SphereCast(ray, m_Radius, out RaycastHit hit, movementThisFrame, m_TargetLayerMask))
            {
                m_HasHit = true;
                transform.position = hit.point;

                bool targetHit = false;
                
                // ターゲットまたは難易度選択ターゲットへの命中判定
                if (hit.collider.TryGetComponent<TargetObject>(out var target))
                {
                    target.OnHit();
                    targetHit = true;

                    // プレイ中の的命中時に FloatingText を生成
                    if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
                    {
                        Color textColor = m_Controller != null ? m_Controller.HitTextColor : Color.green;
                        CreateFloatingText("+100", textColor, hit.point);
                    }
                }
                else if (hit.collider.TryGetComponent<DifficultySelectTarget>(out var diffTarget))
                {
                    diffTarget.OnHit();
                    targetHit = true;
                }

                // 命中時のバイブレーション通知
                if (targetHit && m_Controller != null)
                {
                    m_Controller.TriggerHitHaptic();
                }

                DestroyProjectile();
                return;
            }

            // 移動の適用
            transform.position = nextPosition;
            m_DistanceTraveled += movementThisFrame;

            // 最大射程距離を超えた場合は消滅処理へ
            if (m_DistanceTraveled >= m_MaxDistance)
            {
                // どこにも当たらなかった場合のスコアペナルティと FloatingText の生成
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
                {
                    GameManager.Instance.AddScore(-10);
                    Color textColor = m_Controller != null ? m_Controller.MissTextColor : Color.red;
                    CreateFloatingText("-10", textColor, transform.position);
                }
                DestroyProjectile();
            }
        }

        /// <summary>
        /// 飛翔体を破棄します。TrailRenderer の余韻を残すため、ビジュアルのみを非表示にして時間差で Destroy します。
        /// </summary>
        private void DestroyProjectile()
        {
            m_HasHit = true;

            // 自身のメッシュレンダラーを無効化（TrailRenderer以外）
            if (TryGetComponent<Renderer>(out var renderer))
            {
                if (!(renderer is TrailRenderer))
                {
                    renderer.enabled = false;
                }
            }

            // 子オブジェクトのレンダラーも非表示にする（TrailRenderer以外）
            var childRenderers = GetComponentsInChildren<Renderer>();
            foreach (var r in childRenderers)
            {
                if (r != null && !(r is TrailRenderer))
                {
                    r.enabled = false;
                }
            }

            // TrailRenderer の時間経過を待ってからオブジェクトを削除
            float destroyDelay = 0.5f;
            if (TryGetComponent<TrailRenderer>(out var trail))
            {
                destroyDelay = trail.time;
            }
            else
            {
                var childTrail = GetComponentInChildren<TrailRenderer>();
                if (childTrail != null)
                {
                    destroyDelay = childTrail.time;
                }
            }

            Destroy(gameObject, destroyDelay);
        }

        /// <summary>
        /// 指定されたテキストと色で、特定の座標にフローティングテキストを生成します。
        /// </summary>
        private void CreateFloatingText(string text, Color color, Vector3 position)
        {
            GameObject go = new GameObject("FloatingText_Instance");
            go.transform.position = position;
            FloatingText ft = go.AddComponent<FloatingText>();
            ft.Initialize(text, color);
        }
    }
}
