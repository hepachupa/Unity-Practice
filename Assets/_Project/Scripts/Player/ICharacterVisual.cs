using UnityEngine;

namespace LNStudy.Player
{
    /// <summary>
    /// 플레이어 외형(캐릭터) 공통 인터페이스. 전투·펫·교체 시스템은 이것만 보고 동작한다.
    /// - Unity-chan: 휴머노이드 Animator 기반 (PlayerAnimator)
    /// - Shisa 같은 리그 없는 모델: 절차적 애니메이션 (ProceduralCharacterAnimator)
    /// </summary>
    public interface ICharacterVisual
    {
        /// <summary>휴머노이드면 Animator, 아니면 null (펫이 어깨 뼈를 찾을 때 사용)</summary>
        Animator HumanoidAnimator { get; }
        /// <summary>발 기준 어깨 높이 (뼈가 없을 때 펫 위치 기준)</summary>
        float ShoulderHeight { get; }
        void PlayAttack();
        void PlaySkill();
        void PlayHit();
    }
}
