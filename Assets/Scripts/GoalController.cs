using UnityEngine;

public class GoalController : MonoBehaviour
{
    //UIManagerを保持する変数
    private UIManager uiManager;

    void Start()
    {
        uiManager = Object.FindFirstObjectByType<UIManager>();
    }

    void Update()
    {
        
    }

    public void OnTriggerEnter2D(Collider2D collision){
        if(collision.CompareTag("Player")){
            Debug.Log("Goal!");

            // UIManagerが見つかっていれば、直接描画処理を呼び出す！
            if (uiManager != null)
            {
                uiManager.GameClear();
            }else{
                Debug.Log("uiManager が見つからないよ");
            }
        }
    }
}
