using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace Unity.XR.XREAL.Samples
{
    /// <summary>
    /// ゲーム全体の流れを管理するマネージャークラス。
    /// ゲーム開始時にコントローラーまたはハンドトラッキングのキャリブレーション（開始待機）を要求し、
    /// 完了後にゲームをアクティブにします。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        /// <summary>
        /// GameManagerのシングルトンインスタンス
        /// </summary>
        public static GameManager Instance { get; private set; }



        [Header("ゲーム状態")]
        [SerializeField]
        private GameState m_CurrentState = GameState.Calibration;

        /// <summary>
        /// 現在のゲームの進行状態
        /// </summary>
        public GameState CurrentState => m_CurrentState;

        [Header("ゲームモード設定")]
        [Tooltip("生成する的のプレハブ")]
        [SerializeField]
        private TargetObject m_TargetPrefab;

        [Tooltip("ゲーム開始時に生成する的のプールサイズ")]
        [SerializeField]
        private int m_PoolSize = 10;

        [Tooltip("的がポップする候補位置のTransform配列")]
        [SerializeField]
        private Transform[] m_SpawnPoints;

        [Tooltip("ゲームの制限時間（秒）")]
        [SerializeField]
        private float m_GameDuration = 30f;

        [Tooltip("プレイヤーの最大ライフ（3回までOK、4回目でゲームオーバー）")]
        [SerializeField]
        private int m_MaxLife = 3;

        [Tooltip("同時にフィールドに存在できる的の最大数")]
        [SerializeField]
        private int m_MaxActiveTargets = 3;

        [Tooltip("的がポップする間隔（秒）")]
        [SerializeField]
        private float m_SpawnInterval = 1.5f;

        [Header("オーディオ設定")]
        [Tooltip("ダメージを受けたときのSE")]
        [SerializeField]
        private AudioClip m_DamageSound;

        [Tooltip("クリア時に順番に再生するSEの配列（例: クラッカー音 → ファンファーレ等）")]
        [SerializeField]
        private AudioClip[] m_ClearSounds;

        private AudioSource m_AudioSource;

        [Header("BGM設定")]
        [Tooltip("BGM再生用のAudioSource")]
        [SerializeField]
        private AudioSource m_BGMSource;

        [Tooltip("BGMのオーディオクリップ")]
        [SerializeField]
        private AudioClip m_BGMClip;

        [Tooltip("特定の秒数間でループさせるか")]
        [SerializeField]
        private bool m_UseCustomLoop = false;

        [Tooltip("ループ開始秒数")]
        [SerializeField]
        private float m_LoopStartTime = 0f;

        [Tooltip("ループ終了秒数")]
        [SerializeField]
        private float m_LoopEndTime = 100f;

        [Header("ゲーム進行設定")]
        [Tooltip("ゲームオーバー後、タップに反応しないクールタイム（秒）")]
        [SerializeField]
        private float m_GameOverCooldown = 2.0f;

        private float m_GameOverTime = -100f;

        [Header("エフェクト設定")]
        [Tooltip("ゲームクリア時に再生する紙吹雪パーティクル")]
        [SerializeField]
        private ParticleSystem m_ConfettiEffect;

        [Header("UI設定")]
        [Tooltip("画面中央等に案内テキストを表示するためのTextMeshProテキスト")]
        [SerializeField]
        private TMP_Text m_GuidanceText;

        [Tooltip("スコア表示用の専用TextMeshProテキスト（ゼロ埋め表示）")]
        [SerializeField]
        private TMP_Text m_ScoreText;

        [Tooltip("残り時間表示用の専用TextMeshProテキスト（ss.SSS形式）")]
        [SerializeField]
        private TMP_Text m_TimerText;

        [Tooltip("残り時間を表示するゲージ（Image: Filled指定）")]
        [SerializeField]
        private Image m_TimerGauge;

        [Tooltip("Beam Proモードのキャリブレーション中に表示する案内メッセージ")]
        [SerializeField]
        [TextArea(2, 4)]
        private string m_CalibrationMessage = "真正面にBeam Proを向けて、\n画面をタップしてください";

        [Tooltip("キャリブレーション完了時に一時表示するメッセージ")]
        [SerializeField]
        private string m_StartMessage = "START!";

        private XREALActions m_InputActions;
        private TargetObject[] m_Targets;

        // ゲーム進行管理用変数
        private float m_TimeRemaining;
        private int m_Score;
        private int m_CurrentLife;
        private float m_SpawnTimer;

        private void Awake()
        {
            // シングルトンの初期化
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            // 自動生成された XREALActions のインスタンス化
            m_InputActions = new XREALActions();

            // オーディオソースの取得または追加
            m_AudioSource = GetComponent<AudioSource>();
            if (m_AudioSource == null)
            {
                m_AudioSource = gameObject.AddComponent<AudioSource>();
            }
        }

        private void OnEnable()
        {
            if (m_InputActions != null)
            {
                m_InputActions.Enable();
            }
        }

        private void OnDisable()
        {
            if (m_InputActions != null)
            {
                m_InputActions.Disable();
            }
        }

        private void Start()
        {
            // 起動時に HMD を 6DoF モードに強制設定する
            if (XREALPlugin.GetTrackingType() != TrackingType.MODE_6DOF)
            {
                Debug.Log("[XREAL] HMDのトラッキングを6DoFモードに切り替えます...");
                _ = XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_6DOF, (result, targetMode) => {
                    Debug.Log($"[XREAL] 6DoF切り替え結果: {result}, 現在のモード: {XREALPlugin.GetTrackingType()}");
                });
            }

            // プレハブから的をインスタンス化してプールを構築
            if (m_TargetPrefab != null)
            {
                m_Targets = new TargetObject[m_PoolSize];
                for (int i = 0; i < m_PoolSize; i++)
                {
                    TargetObject instance = Instantiate(m_TargetPrefab, Vector3.zero, Quaternion.identity, transform);
                    instance.gameObject.SetActive(false);
                    m_Targets[i] = instance;
                }
                Debug.Log($"[GameManager] {m_PoolSize}個の的オブジェクトをインスタンス化し、プールに登録しました。");
            }
            else
            {
                Debug.LogError("[GameManager] TargetPrefab が設定されていません！インスペクターから設定してください。");
                m_Targets = new TargetObject[0];
            }

            // 開始時に案内UIを表示し、メッセージを設定
            m_GuidanceText.text = m_CalibrationMessage;
            m_GuidanceText.gameObject.SetActive(true);

            // BGMの再生開始
            StartBGM();

            // PCデバッグ用に初期状態ではカーソルを表示
            SetCursorState(false);
        }

        private void StartBGM()
        {
            if (m_BGMSource == null)
            {
                m_BGMSource = gameObject.AddComponent<AudioSource>();
            }

            if (m_BGMClip != null)
            {
                m_BGMSource.clip = m_BGMClip;
                m_BGMSource.loop = true; // デフォルトでループさせる
                m_BGMSource.volume = 0.5f; // 適当な初期音量
                m_BGMSource.Play();
            }
        }

        private void Update()
        {
            // BGMのカスタムループ処理
            UpdateBGMLoop();

            // キャリブレーション（開始待機）状態のときのみ、入力を監視して実行
            if (m_CurrentState == GameState.Calibration)
            {
                if (IsTapDetected())
                {
                    PerformCalibration();
                }
            }
            else if (m_CurrentState == GameState.Playing)
            {
                // 残り時間の減算
                m_TimeRemaining -= Time.deltaTime;
                if (m_TimeRemaining <= 0f)
                {
                    m_TimeRemaining = 0f;
                    UpdatePlayingUI(); // 最後に0秒になったUIを反映させておく
                    TriggerGameOver(true); // 時間切れはクリア扱い
                    return; // UIを上書きしないようにここで処理を抜ける
                }

                // スポーン処理のタイマー更新
                m_SpawnTimer += Time.deltaTime;
                if (m_SpawnTimer >= m_SpawnInterval)
                {
                    m_SpawnTimer = 0f;
                    TrySpawnTarget();
                }

                // UIの更新
                UpdatePlayingUI();
            }
            else if (m_CurrentState == GameState.GameOver)
            {
                // ゲームオーバー直後はタップを無視する（数秒間）
                if (Time.time - m_GameOverTime >= m_GameOverCooldown)
                {
                    // ゲームオーバー時の画面タップでキャリブレーションに戻る
                    if (IsTapDetected())
                    {
                        ReturnToCalibration();
                    }
                }
            }
        }

        /// <summary>
        /// BGMの任意区間ループ処理を更新します。
        /// </summary>
        private void UpdateBGMLoop()
        {
            if (!m_UseCustomLoop || m_BGMSource == null || m_BGMSource.clip == null || !m_BGMSource.isPlaying) return;

            // 終了時間に到達した場合は開始時間に戻す
            if (m_BGMSource.time >= m_LoopEndTime)
            {
                m_BGMSource.time -= (m_LoopEndTime - m_LoopStartTime);
            }
            // 標準のループで勝手に先頭(0秒)に戻ってしまった場合の対策
            else if (m_BGMSource.time < m_LoopStartTime && m_BGMSource.time < 0.2f)
            {
                m_BGMSource.time = m_LoopStartTime;
            }
        }

        /// <summary>
        /// プレイ中のUI表示（時間、スコア、ライフ）をリアルタイム更新します。
        /// </summary>
        private void UpdatePlayingUI()
        {
            // インストラクションテキストからは時間とスコアを外し、ライフのみ表示する
            m_GuidanceText.gameObject.SetActive(true);
            m_GuidanceText.text = $"LIFE: {m_CurrentLife}";

            // 専用UIの更新
            if (m_ScoreText != null)
            {
                m_ScoreText.text = $"SCORE: {m_Score:00000}";
            }

            if (m_TimerText != null)
            {
                // 00.000形式で表示 (ss.SSS)
                m_TimerText.text = m_TimeRemaining.ToString("00.000");
            }

            if (m_TimerGauge != null)
            {
                // 最大時間に対する残り時間の割合でゲージを更新
                m_TimerGauge.fillAmount = Mathf.Clamp01(m_TimeRemaining / m_GameDuration);
            }
        }

        /// <summary>
        /// 非アクティブな的をプールから選択し、ランダムな位置・移動パターンでポップさせます。
        /// </summary>
        private void TrySpawnTarget()
        {
            if (m_Targets == null || m_Targets.Length == 0) return;
            if (m_SpawnPoints == null || m_SpawnPoints.Length == 0) return;

            // 現在アクティブな的の数を数える
            int activeCount = 0;
            foreach (var target in m_Targets)
            {
                if (target.gameObject.activeInHierarchy)
                {
                    activeCount++;
                }
            }

            // 同時存在数制限を超えている場合はスポーンしない
            if (activeCount >= m_MaxActiveTargets) return;

            // 非アクティブな的をランダムに探す
            TargetObject spawnTarget = null;
            int startIndex = Random.Range(0, m_Targets.Length);
            for (int i = 0; i < m_Targets.Length; i++)
            {
                int index = (startIndex + i) % m_Targets.Length;
                var target = m_Targets[index];
                if (!target.gameObject.activeInHierarchy)
                {
                    spawnTarget = target;
                    break;
                }
            }

            // 空きの的があればポップ
            if (spawnTarget != null)
            {
                Transform spawnPoint = m_SpawnPoints[Random.Range(0, m_SpawnPoints.Length)];
                TargetMovementPattern pattern = (TargetMovementPattern)Random.Range(0, 3);

                Debug.Log($"[GameManager] 的 {spawnTarget.name} をポップします。地点: {spawnPoint.name}, パターン: {pattern}");
                spawnTarget.Spawn(spawnPoint.position, pattern);
            }
        }

        /// <summary>
        /// スコアを加算します（的オブジェクトの命中時に呼ばれます）。
        /// </summary>
        public void AddScore(int points)
        {
            if (m_CurrentState != GameState.Playing) return;
            m_Score += points;
            
            // スコアがマイナスにならないように0で下限を設ける
            if (m_Score < 0) m_Score = 0;
            
            UpdatePlayingUI();
        }

        /// <summary>
        /// 的がプレイヤーに到達したときに呼ばれ、ライフを減らします。
        /// </summary>
        public void OnTargetReachPlayer(TargetObject target)
        {
            if (m_CurrentState != GameState.Playing) return;

            m_CurrentLife--;
            Debug.Log($"[GameManager] 的がプレイヤーに到達しました。残りライフ: {m_CurrentLife}");

            // 被弾ペナルティ（-100点）
            AddScore(-100);

            // ダメージSEの再生
            if (m_DamageSound != null && m_AudioSource != null)
            {
                m_AudioSource.PlayOneShot(m_DamageSound);
            }

            if (m_CurrentLife < 0)
            {
                TriggerGameOver(false); // ライフ0はゲームオーバー扱い
            }
            else
            {
                UpdatePlayingUI();
            }
        }

        /// <summary>
        /// ゲーム終了処理を行います。
        /// </summary>
        /// <param name="isClear">時間切れでクリアした場合はtrue</param>
        private void TriggerGameOver(bool isClear)
        {
            Debug.Log($"[GameManager] ゲーム終了（クリア: {isClear}）");
            m_CurrentState = GameState.GameOver;
            m_GameOverTime = Time.time;

            // PCデバッグ用にカーソルを表示・ロック解除
            SetCursorState(false);

            // すべての的を非アクティブにする
            foreach (var target in m_Targets)
            {
                target.gameObject.SetActive(false);
            }

            // 最終結果を表示
            m_GuidanceText.gameObject.SetActive(true);
            
            if (isClear)
            {
                m_GuidanceText.text = $"GAME CLEAR!!\nFINAL SCORE: {m_Score}\n画面をタップして再挑戦";
                if (m_ConfettiEffect != null)
                {
                    m_ConfettiEffect.Play();
                }

                // クリア音を順番に再生
                StartCoroutine(PlayClearSoundsRoutine());
                
                // 虹色グラデーションアニメーションを開始
                StartCoroutine(RainbowTextRoutine());
            }
            else
            {
                m_GuidanceText.text = $"GAME OVER!\nFINAL SCORE: {m_Score}\n画面をタップして再挑戦";
            }
        }

        private IEnumerator PlayClearSoundsRoutine()
        {
            if (m_ClearSounds == null || m_AudioSource == null) yield break;

            foreach (var clip in m_ClearSounds)
            {
                if (clip != null)
                {
                    m_AudioSource.PlayOneShot(clip);
                    yield return new WaitForSeconds(clip.length);
                }
            }
        }

        private IEnumerator RainbowTextRoutine()
        {
            // グラデーションを有効化
            m_GuidanceText.enableVertexGradient = true;

            while (m_CurrentState == GameState.GameOver)
            {
                // 時間の経過に合わせて色相（Hue）を少しずらして4頂点の色を生成
                float t = Time.time * 0.8f; 
                Color topLeft = Color.HSVToRGB(Mathf.Repeat(t, 1f), 0.8f, 1f);
                Color topRight = Color.HSVToRGB(Mathf.Repeat(t + 0.2f, 1f), 0.8f, 1f);
                Color bottomLeft = Color.HSVToRGB(Mathf.Repeat(t + 0.4f, 1f), 0.8f, 1f);
                Color bottomRight = Color.HSVToRGB(Mathf.Repeat(t + 0.6f, 1f), 0.8f, 1f);

                m_GuidanceText.colorGradient = new VertexGradient(topLeft, topRight, bottomLeft, bottomRight);

                yield return null; // 毎フレーム更新
            }

            // 状態が変わったらグラデーションを無効化して白に戻す
            m_GuidanceText.enableVertexGradient = false;
            m_GuidanceText.color = Color.white;
        }

        /// <summary>
        /// キャリブレーション（最初）の状態へ戻ります。
        /// </summary>
        private void ReturnToCalibration()
        {
            Debug.Log("[GameManager] キャリブレーション待ちへ戻ります。");
            m_CurrentState = GameState.Calibration;

            // 紙吹雪を即座に停止し、画面に残っているものもすべて消去する
            if (m_ConfettiEffect != null)
            {
                m_ConfettiEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            // PCデバッグ用にカーソルを表示・ロック解除
            SetCursorState(false);

            m_GuidanceText.text = m_CalibrationMessage;
            m_GuidanceText.gameObject.SetActive(true);
        }

        /// <summary>
        /// PCデバッグ中（エディタ実行時）のマウスカーソルの表示・ロック状態を制御します。
        /// </summary>
        private void SetCursorState(bool locked)
        {
            if (Application.isEditor)
            {
                if (locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }

        /// <summary>
        /// ゲームを初期状態（キャリブレーション待ち）に戻します（Appボタン等から手動で呼び出されます）。
        /// </summary>
        public void ResetGame()
        {
            Debug.Log("[GameManager] 手動によるゲームリセット要求を受信しました。");
            
            // すべての的を非アクティブにする
            foreach (var target in m_Targets)
            {
                target.gameObject.SetActive(false);
            }

            ReturnToCalibration();
        }

        /// <summary>
        /// 開始用のタップが行われたか判定します。
        /// </summary>
        private bool IsTapDetected()
        {
            // 1. XREALActions による Trigger 検知 (コントローラーのトリガー/画面タップに対応)
            if (m_InputActions != null && m_InputActions.XREALButtons.Trigger.WasPressedThisFrame())
            {
                return true;
            }

            // 2. Pointer API（画面タップ / マウスクリック）による検知 (エディタテスト用)
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// キャリブレーション（または開始待機）を終了し、ゲームを開始します。
        /// </summary>
        private void PerformCalibration()
        {
            Debug.Log("[GameManager] キャリブレーション完了。ゲームを開始します。");

            // コントローラーのリセンターを実行
            XREALPlugin.RecenterController();

            // インスペクターの設定値の安全チェック（シリアライズ破壊対策）
            if (m_GameDuration <= 0f) m_GameDuration = 30f;
            if (m_MaxLife <= 0) m_MaxLife = 3;
            if (m_MaxActiveTargets <= 0) m_MaxActiveTargets = 3;
            if (m_SpawnInterval <= 0f) m_SpawnInterval = 1.5f;

            // ゲームパラメータの初期化
            m_TimeRemaining = m_GameDuration;
            m_Score = 0;
            m_CurrentLife = m_MaxLife;
            m_SpawnTimer = 0f;

            // 的プールを初期状態に戻す
            foreach (var target in m_Targets)
            {
                target.ResetTarget();
                target.gameObject.SetActive(false);
            }

            // 状態をゲームプレイ中に移行
            m_CurrentState = GameState.Playing;

            // PCデバッグ用にカーソルを非表示・ロック
            SetCursorState(true);

            // 開始演出コール
            StartCoroutine(ShowStartMessageRoutine());
        }

        private IEnumerator ShowStartMessageRoutine()
        {
            m_GuidanceText.text = m_StartMessage;
            yield return new WaitForSeconds(1.0f);
            m_GuidanceText.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (m_InputActions != null)
            {
                m_InputActions.Dispose();
            }
        }
    }

    /// <summary>
    /// ゲームの進行状態を示す列挙型
    /// </summary>
    public enum GameState
    {
        /// <summary> キャリブレーション中（開始待ち） </summary>
        Calibration,
        /// <summary> ゲームプレイ中 </summary>
        Playing,
        /// <summary> ゲーム終了 </summary>
        GameOver
    }
}
