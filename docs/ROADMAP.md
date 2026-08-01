# ROADMAP — 단계별 개발 계획

각 Phase는 독립적으로 완결·시연 가능해야 한다. Phase 완료 시 이 파일의 체크박스를 갱신할 것.

## Phase 1 — Core 도메인 + 규칙 로더 (UI 없음)
- [ ] `HfeLayoutSim.Core` 프로젝트: Layout/LayoutElement/Semantics/CanvasSpec 모델
- [ ] `*.hfelayout.json` 직렬화/역직렬화 (System.Text.Json, 스키마 검증 포함)
- [ ] `rules/hfe_rules.json` 로더 + RuleDefinition 모델 (파싱 오류 시 규칙 ID·경로 명시)
- [ ] `UnitConverter` (mm↔px, pt↔px, 96dpi)
- [ ] `ContrastCalculator` — SPEC §6.1 검증 케이스 7건 단위 테스트 통과
- [ ] 샘플 레이아웃 JSON 2개 수작성: 수입검사 성적서(Paper), MES 검사수행(Screen)

## Phase 2 — 평가 엔진
- [ ] `IRuleEvaluator` 인터페이스 + check 종류별 구현체 (C1~C9, SPEC §6.2 표 순)
- [ ] `HfeEngine.Evaluate(Layout, RuleSet) → EvaluationReport`
- [ ] 채점: 카테고리 점수, 가중 총점, 등급, Critical 등급 상한(C) 규칙
- [ ] Finding 5요소(규칙ID/실측vs기준/요소ID/권고/근거) 완전성
- [ ] 단위 테스트: 규칙별 위반/통과/N.A. 최소 3케이스 — **의도적으로 나쁜 레이아웃**
      (예: LOT를 좌하단에, 체크박스 12연속, #F57C00 텍스트)을 만들어 검출 확인
- [ ] 콘솔 러너(`dotnet run -- evaluate a.hfelayout.json`)로 엔진 단독 시연

## Phase 3 — WPF 편집기 (설계 기능)
- [ ] MainWindow 3분할: 팔레트 / 캔버스 / 속성 패널
- [ ] 캔버스: 드래그 배치, 선택/다중 선택, 리사이즈 핸들, 스냅(1mm/8px), 줌
- [ ] 속성 패널: 기하·스타일·Semantics 편집 (Role/sequence/groupId/isCritical/judgment)
- [ ] 구역 오버레이(POA/SFA/WFA/TA), 여백 가이드, Screen 고정영역 가이드
- [ ] Undo/Redo(50단계), 자동 저장(30초), 파일 열기/저장
- [ ] 템플릿 2종(8블록 성적서 Paper / MES 검사수행 Screen)에서 새로 만들기

## Phase 4 — 평가 리포트 UI
- [ ] [평가 실행] → 비동기 평가 → 리포트 패널: 총점·등급·카테고리 바, Finding 목록
- [ ] Finding 클릭 → 캔버스 해당 요소 스크롤+하이라이트(3회 점멸 후 고정)
- [ ] 심각도/카테고리 필터, 강점(Strengths) 섹션
- [ ] HTML 리포트 내보내기(자체 템플릿, 인쇄 대응)

## Phase 5 — 비교·시뮬레이션
- [ ] Variant 복제(variantOf 연결), 최대 3개 병렬 비교 뷰
- [ ] 카테고리 레이더 차트(자체 Canvas 렌더링), 총점 막대
- [ ] Finding diff(해소/신규/공통 잔존)
- [ ] (선택) 배치 자동 제안: 위반 Finding에 "자동 수정 시도" — 예: WFA의 경고 박스를
      TA로 이동 제안 미리보기

## 이후 백로그 (v2 후보)
- 시선 흐름 시뮬레이션 오버레이(현저성 맵 근사: 크기·대비·색 가중 히트맵)
- NASA-TLX 평가 세션 기록 모듈(실사용자 평가 입력·양식 연동, MNL §7.1)
- 규칙 편집 UI(JSON 직접 편집 대체)
- MES 목업 HTML(별첨) ↔ 레이아웃 JSON 상호 변환
