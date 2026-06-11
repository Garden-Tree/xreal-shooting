using UnityEngine;

namespace Unity.XR.XREAL.Samples
{
    public enum GameDifficulty
    {
        EASY,
        NORMAL,
        HARD
    }

    /// <summary>
    /// 難易度選択用の的（Diegetic UI）にアタッチするスクリプト。
    /// BeamProControllerで撃たれた際に、GameManagerに難易度を通知してゲームを開始します。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DifficultySelectTarget : MonoBehaviour
    {
        [Tooltip("この的を撃ったときに設定される難易度")]
        [SerializeField]
        private GameDifficulty m_Difficulty = GameDifficulty.NORMAL;

        [Header("エフェクト設定")]
        [Tooltip("被弾時に再生するパーティクルシステム（任意）")]
        [SerializeField]
        private ParticleSystem m_HitEffect;

        [Tooltip("被弾時に再生する効果音（SE）")]
        [SerializeField]
        private AudioClip m_HitSound;

        private AudioSource m_AudioSource;
        private bool m_IsHit = false;

        private void Awake()
        {
            m_AudioSource = GetComponent<AudioSource>();
            if (m_AudioSource == null)
            {
                m_AudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        public void OnHit()
        {
            if (m_IsHit) return;
            m_IsHit = true;

            // エフェクト再生
            if (m_HitEffect != null)
            {
                ParticleSystem effectInstance = Instantiate(m_HitEffect, transform.position, transform.rotation);
                effectInstance.gameObject.SetActive(true);
                effectInstance.Play();
                Destroy(effectInstance.gameObject, 2.0f);
            }

            // SE再生
            if (m_HitSound != null && m_AudioSource != null)
            {
                m_AudioSource.PlayOneShot(m_HitSound);
            }

            // GameManagerに難易度を伝えてゲームスタート
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartGameWithDifficulty(m_Difficulty);
            }
            
            // 自身を含む難易度選択UI群を非表示にする処理はGameManager側で行われる前提
        }

        public void ResetTarget()
        {
            m_IsHit = false;
        }
    }
}
