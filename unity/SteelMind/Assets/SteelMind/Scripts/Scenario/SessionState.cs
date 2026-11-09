using UnityEngine;

namespace SteelMind.Scenario
{
    /// <summary>
    /// 한 번 플레이하는 동안만 남는 기록(장면을 바꿔도 유지). 메인 화면·인트로를 한 번만 보여 주는 데 쓴다.
    /// Domain Reload가 꺼져 있어도 플레이를 멈추면 이 오브젝트가 사라지므로 static 대신 이것을 쓴다.
    /// </summary>
    public class SessionState : MonoBehaviour
    {
        public bool titleShown;

        /// <summary>열간압연 시나리오의 보고서를 닫았는가. 켜지면 전체 공정 돌발 문제가 시작된다.</summary>
        public bool hotRollingDone;

        /// <summary>사건별로 바로 전 판에 나온 상황. 다음 판에 같은 상황이 나오지 않게 한다.</summary>
        public readonly System.Collections.Generic.Dictionary<string, int> lastVariant = new System.Collections.Generic.Dictionary<string, int>();
    }
}
