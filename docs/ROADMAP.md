# ROADMAP — 단계별 개발 계획

각 Phase는 독립적으로 완결·시연 가능해야 한다. Phase 완료 시 이 파일의 체크박스를 갱신할 것.

## Phase 1 — Core 도메인 + 규칙 로더 (UI 없음) ✅ 완료
- [x] `HfeLayoutSim.Core` 프로젝트: Layout/LayoutElement/Semantics/CanvasSpec 모델
- [x] `*.hfelayout.json` 직렬화/역직렬화 (System.Text.Json, 스키마 검증 포함)
- [x] `rules/hfe_rules.json` 로더 + RuleDefinition 모델 (파싱 오류 시 규칙 ID·경로 명시)
- [x] `UnitConverter` (mm↔px, pt↔px, 96dpi)
- [x] `ContrastCalculator` — SPEC §6.1 검증 케이스 7건 단위 테스트 통과
- [x] 샘플 레이아웃 JSON 수작성: 수입검사 성적서(Paper), MES 검사수행(Screen)
      + 의도적 불량 레이아웃(`samples/bad_layout_paper.hfelayout.json`) 추가

## Phase 2 — 평가 엔진 ✅ 완료
- [x] `IRuleEvaluator` 인터페이스 + check 종류별 구현체 (C1~C9, SPEC §6.2 표 순 — 47개 check)
- [x] `HfeEngine.Evaluate(Layout, RuleSet) → EvaluationReport`
- [x] 채점: 카테고리 점수, 가중 총점, 등급, Critical 등급 상한(C) 규칙, isCritical 요소 심각도 상향
- [x] Finding 5요소(규칙ID/실측vs기준/요소ID/권고/근거) 완전성
- [x] 단위 테스트: 규칙별 위반/통과/N.A. 케이스 — **의도적으로 나쁜 레이아웃**
      (LOT를 좌하단에, 체크박스 12연속, #F57C00 텍스트, Precondition 후행)에서 16개 규칙 검출 확인
      — 총 113개 테스트 통과
- [x] 콘솔 러너(`dotnet run --project src/HfeLayoutSim.Cli -- evaluate a.hfelayout.json`)로 엔진 단독 시연

## Phase 3 — WPF 편집기 (설계 기능) — 1차 버전 완료(2026-08), CI가 exe 자동 빌드
- [x] MainWindow 3분할: 팔레트 / 캔버스 / 우측 패널(코칭·속성·리포트 탭)
- [x] 캔버스: 팔레트 더블클릭 배치, 선택, 드래그 이동, 스냅(1mm/8px), 줌 슬라이더
      — 다중 선택·리사이즈 핸들은 v2
- [x] 속성 패널: 문구·기하(X/Y/W/H) 편집, 요소 삭제 — 스타일·Semantics 편집 UI는 v2
- [x] 구역 오버레이(POA/SFA/WFA/TA) 토글
- [x] 파일 열기/저장 — Undo/Redo·자동 저장은 v2
- [x] 새로 만들기(Paper/Screen) + **검사 카탈로그에서 생성** (템플릿 프리셋 대체·상회)
- [x] ★ 배치 코칭 연동: 배치/이동/선택 시 PlacementAdvisor 진단이 코칭 탭에 실시간 표시
- [x] ★ GitHub Actions(windows-latest)가 push마다 self-contained exe 아티팩트
      "HfeLayoutSim-win64" 생성 — Actions 탭에서 zip 다운로드

## Phase 4 — 평가 리포트 UI — 1차 버전 완료
- [x] [평가 실행] → 리포트 탭: PASS/FAIL 배너·총점·등급·카테고리 등급, Finding 목록
      (비동기 실행은 v2 — 현재 규모에서는 <1초)
- [x] Finding 클릭 → 캔버스 해당 요소 하이라이트 (스크롤·점멸 연출은 v2)
- [ ] 심각도/카테고리 필터, 강점(Strengths) 섹션 UI
- [x] HTML 리포트 내보내기(자체 템플릿, 인쇄 대응) — **Core `HtmlReportExporter` + CLI `--html` 완료**,
      WPF 버튼 연동만 잔여

## Phase 5 — 비교·시뮬레이션
- [ ] Variant 복제(variantOf 연결), 최대 3개 병렬 비교 뷰
- [ ] 카테고리 레이더 차트(자체 Canvas 렌더링), 총점 막대 — 데이터는
      `VariantSummary.CategoryScores`(C1~C9)로 준비 완료, 렌더링만 잔여
- [x] Finding diff(해소/신규/공통 잔존) — **Core `VariantComparer`(2~3개) + CLI `compare` 완료**,
      WPF 뷰 연동만 잔여
- [ ] (선택) 배치 자동 제안: 위반 Finding에 "자동 수정 시도" — 예: WFA의 경고 박스를
      TA로 이동 제안 미리보기

## 품질·보강 작업 (2026-08 완료)
- [x] 매뉴얼 §8.1 통합 체크리스트 ↔ 규칙 커버리지 매핑 (`docs/RULE_COVERAGE.md`) — 갭 2건 규칙 신설
      (C7-07 서명 이원 구성, C8-07 판정 선택지 완전성 → 총 53규칙)
- [x] 규칙 튜닝 회귀 도구 — `RuleRegression` Core + CLI `regress` (임계값 변경 영향을 증거로 검토)
- [x] 요소 프리셋 라이브러리 (`rules/element_presets.json`) — 팔레트 기본값·권장 구역·가이드,
      사용자 커스터마이징 가능
- [x] 배치 어드바이저 — `PlacementAdvisor` Core + CLI `advise`: 요소 하나 배치할 때마다
      위치 적정성/권장 좌표/색상/폰트/크기/아이콘/순서 오류 위험을 코칭 (WPF 드롭 이벤트에 연결 예정)
- [x] 검사 카탈로그 → 레이아웃 생성 — `InspectionCatalog`+`LayoutComposer` Core + CLI `generate`:
      공정·항목·기준·이미지·알람을 사용자가 JSON으로 정의하면 8블록 표준 준수 레이아웃(Paper/Screen)
      자동 생성 (생성 결과 A등급 PASS 보장 테스트 포함)
- [x] Verifier식 판정 표시 — PASS/FAIL 배너 + 카테고리별 등급(바코드 검증기 스타일), rules JSON
      `passGrade` 기준 — CLI·HTML 공통, WPF 리포트 패널도 동일 디자인 적용 예정

## 이후 백로그 (v2 후보)
- 시선 흐름 시뮬레이션 오버레이(현저성 맵 근사: 크기·대비·색 가중 히트맵)
- NASA-TLX 평가 세션 기록 모듈(실사용자 평가 입력·양식 연동, MNL §7.1)
- 규칙 편집 UI(JSON 직접 편집 대체)
- MES 목업 HTML(별첨) ↔ 레이아웃 JSON 상호 변환
