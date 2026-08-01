# Claude Code 첫 프롬프트 (복사해서 사용)

아래 내용을 Claude Code 첫 메시지로 붙여넣으세요. (이 핸드오프 폴더를 프로젝트 루트로
사용한다는 전제 — CLAUDE.md, docs/, rules/가 이미 있는 상태)

---

이 저장소는 "HFE Layout Simulator" 프로젝트의 핸드오프 패키지야.
CLAUDE.md, docs/SPEC.md, docs/ROADMAP.md, docs/DESIGN_TOKENS.md, rules/hfe_rules.json을
먼저 정독하고 시작해줘.

**목표**: 의료기기 QC 검사 양식(A4 종이)과 MES 화면(1920×1080) 레이아웃을 캔버스에서
조합·설계하고, [평가 실행] 시 rules/hfe_rules.json의 HFE 규칙(9개 카테고리, 60여 규칙)으로
채점하여 강점/취약점을 상세 분석하는 .NET 8 WPF 프로그램.

**지금 할 일**: ROADMAP의 Phase 1(Core 도메인 + 규칙 로더)을 구현해줘.
- 솔루션 구조는 CLAUDE.md의 구조를 그대로 따를 것
- ContrastCalculator는 SPEC §6.1의 검증 케이스 7건을 xUnit 테스트로 먼저 작성(TDD)
- 규칙 수치는 절대 하드코딩하지 말고 rules/hfe_rules.json에서 로드
- 완료 후 `dotnet test` 전체 통과를 보여주고, ROADMAP 체크박스를 갱신해줘

Phase 1이 끝나면 멈추고 결과를 요약해줘. Phase 2는 내가 검토 후 지시할게.

---

## 이어지는 프롬프트 예시

- Phase 2: "Phase 2 평가 엔진을 구현해줘. 의도적으로 나쁜 샘플 레이아웃(LOT 좌하단 배치,
  체크박스 12연속, #F57C00 일반 텍스트, Precondition이 항목 뒤)을 만들어 모든 해당 규칙이
  검출되는지 테스트로 증명해줘."
- Phase 3: "WPF 편집기를 만들어줘. 캔버스 스냅과 구역 오버레이(POA/SFA/WFA/TA)부터."
- 규칙 튜닝: "C5-04의 maxRun을 5로 바꾸면 어떤 영향이 있는지 샘플들로 회귀 평가해줘."
- 검증: "매뉴얼 §8.1 통합 체크리스트 15항목과 규칙 커버리지 매핑표를 만들어서
  빠진 체크 항목이 있으면 규칙 추가를 제안해줘."
