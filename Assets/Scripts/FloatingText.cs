using UnityEngine;
using TMPro;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// 3D空間上でふわっと浮き上がりながらフェードアウトするテキスト演出クラス。
    /// 常にプレイヤー（メインカメラ）の方向を向く（ビルボード）処理を含みます。
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public class FloatingText : MonoBehaviour
    {
        private float m_Duration = 1.0f;
        private float m_Speed = 0.8f;
        private Color m_Color;
        private TextMeshPro m_TextMesh;
        private float m_Timer = 0f;

        /// <summary>
        /// フローティングテキストのパラメータを初期化します。
        /// </summary>
        /// <param name="text">表示する文字列</param>
        /// <param name="color">文字色</param>
        /// <param name="duration">表示持続時間（秒）</param>
        /// <param name="speed">上昇速度（m/s）</param>
        public void Initialize(string text, Color color, float duration = 1.0f, float speed = 0.8f)
        {
            m_TextMesh = GetComponent<TextMeshPro>();
            if (m_TextMesh == null)
            {
                m_TextMesh = gameObject.AddComponent<TextMeshPro>();
            }

            m_TextMesh.text = text;
            m_TextMesh.color = color;
            m_TextMesh.fontSize = 5f; // 3D空間で適切に見える大きさ
            m_TextMesh.alignment = TextAlignmentOptions.Center;

            m_Color = color;
            m_Duration = duration;
            m_Speed = speed;
            m_Timer = 0f;

            // 初期フレームでプレイヤーを向かせる
            LookAtCamera();
        }

        private void Update()
        {
            m_Timer += Time.deltaTime;
            if (m_Timer >= m_Duration)
            {
                Destroy(gameObject);
                return;
            }

            // 上方向へ移動
            transform.position += Vector3.up * m_Speed * Time.deltaTime;

            // フェードアウト
            float alpha = Mathf.Clamp01(1f - (m_Timer / m_Duration));
            m_TextMesh.color = new Color(m_Color.r, m_Color.g, m_Color.b, alpha);

            // 常にプレイヤー（カメラ）を向く（ビルボード処理）
            LookAtCamera();
        }

        /// <summary>
        /// テキストが常にカメラを向くように回転を制御します。
        /// </summary>
        private void LookAtCamera()
        {
            if (Camera.main != null)
            {
                // TextMeshProの正面をカメラに向けるため、
                // カメラからテキスト自身に向かうベクトルを前方として LookRotation を設定します。
                Vector3 toText = transform.position - Camera.main.transform.position;
                if (toText.sqrMagnitude > 0.001f)
                {
                    transform.rotation = Quaternion.LookRotation(toText);
                }
            }
        }
    }
}
