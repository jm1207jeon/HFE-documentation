# HFE Layout Simulator (검사 레이아웃 설계·평가 시뮬레이터)

## 프로젝트 개요

의료기기(Nitinol 와이어 스텐트, Hook & Cross) 제조 QC의 **검사 문서·MES 화면 레이아웃을
설계하고, HFE(Human Factors Engineering) 규칙으로 자동 채점·분석하는 Windows 데스크톱 프로그램**.

사용자는 QC 관리자다. 캔버스 위에 요소(제목, 라벨, 입력란, 체크박스, 표, 판정 배지, 버튼,
서명란 등)를 배치해 **종이 검사 양식(A4)** 또는 **MES 화면(1920×1080)** 레이아웃을 구성하고,
[평가 실행]을 누르면 9개 카테고리·60여 개 규칙으로 채점된 상세 분석 리포트
(강점 / 취약점 / 근거 수치 / 개선 권고 / 근거 표준)를 받는다.
여러 배치안(Variant)을 저장해 점수를 비교(A/B, 레이더 차트)하는 것이 핵심 사용 시나리오다.

## 기술 스택 (변경 금지)

- **.NET 8 + WPF**, C# 12
- MVVM: **CommunityToolkit.Mvvm** (Source Generator 방식: `[ObservableProperty]`, `[RelayCommand]`)
- JSON: `System.Text.Json`
- 차트(레이더/막대): 우선 자체 Canvas 렌더링, 필요 시 LiveCharts2 검토
- 테스트: xUnit — **평가 엔진(HfeEngine)은 반드시 단위 테스트 대상**
- 외부 DB 없음. 레이아웃/규칙/리포트는 모두 JSON 파일 (규칙: `rules/hfe_rules.json`)

## 솔루션 구조

```
HfeLayoutSim.sln
├─ src/HfeLayoutSim.Core/        # 도메인 모델 + 평가 엔진 (UI 의존성 0 — 순수 클래스 라이브러리)
│   ├─ Model/                    # Layout, LayoutElement, ElementStyle, Semantics, CanvasSpec ...
│   ├─ Rules/                    # 규칙 로더, RuleDefinition, 임계값
│   ├─ Engine/                   # HfeEngine, 카테고리별 Evaluator (IRuleEvaluator 구현체들)
│   └─ Report/                   # EvaluationReport, Finding, ScoreCard
├─ src/HfeLayoutSim.App/         # WPF 앱 (MVVM)
│   ├─ Views/                    # MainWindow, CanvasView, PaletteView, ReportView, CompareView
│   ├─ ViewModels/
│   └─ Rendering/                # 요소별 DataTemplate/Adorner(선택·리사이즈 핸들)
├─ rules/hfe_rules.json          # ★ 채점 규칙 지식베이스 (사양의 단일 진실 원천)
├─ docs/SPEC.md                  # 상세 요구사항 명세 — 개발 전 반드시 정독
├─ docs/ROADMAP.md               # 단계별 개발 계획 — 이 순서대로 진행
├─ docs/DESIGN_TOKENS.md         # 검증된 색상 팔레트·크기 기준 (매뉴얼 §3 발췌)
└─ tests/HfeLayoutSim.Core.Tests/
```

## 절대 원칙

1. **평가 규칙의 수치를 코드에 하드코딩하지 않는다.** 모든 임계값·가중치·메시지는
   `rules/hfe_rules.json`에서 로드한다. 규칙 추가/조정이 JSON 편집만으로 가능해야 한다.
2. **Core는 WPF를 참조하지 않는다.** 평가 엔진은 Layout JSON을 입력받아 Report를 반환하는
   순수 함수 스타일 — 그래야 단위 테스트와 CLI 배치 평가가 가능하다.
3. **단위 이원화**: 종이 캔버스는 mm, 화면 캔버스는 px이 기본 단위. 내부 저장은 각 매체의
   원 단위로 하고, 규칙 비교 시 `UnitConverter`(96dpi 기준, 1mm=3.7795px)로 정규화한다.
4. **색상 대비율은 WCAG 상대 휘도 공식으로 계산**한다 (docs/SPEC.md §6.1에 공식·검증 케이스
   있음). 테스트: #212529/#FFFFFF=15.43:1, #1565C0/#FFFFFF=5.75:1, #F57C00/#FFFFFF=2.70:1.
5. 요소에는 기하·스타일뿐 아니라 **의미(Semantics: Role, GroupId, Sequence, IsCritical,
   판정표현 3중코딩 여부)**가 있다. 배치 규칙의 절반은 Semantics 기반이다.
6. 리포트의 모든 지적(Finding)은 [규칙 ID + 실측값 vs 기준값 + 해당 요소 ID 목록 +
   개선 권고 + 근거 표준] 5요소를 갖춘다. "나쁨"이라고만 말하는 지적 금지.
7. UI 텍스트는 한국어. 코드 식별자·주석은 영어.

## 도메인 배경 (요약)

이 프로그램의 규칙은 사내 매뉴얼 **MNL-QA-HFE-002 「HFE 문서 및 UI/UX 설계 매뉴얼」**을
기계화한 것이다. 근거 표준: IEC 62366-1(사용적합성), AAMI HE75(설계 데이터),
ISO 9241-110:2020(7대 인터랙션 원칙), WCAG 2.2 AA(대비·타겟 크기·오류 처리),
Nielsen 10 휴리스틱, NASA-TLX(작업부하). 검사 양식은 8블록 골격
(헤더→식별→검사조건 게이트→샘플링→검사항목→부적합기록→종합판정→서명)을 가지며
이 순서 위반이 주요 감점 항목이다. 상세는 docs/SPEC.md와 rules JSON의 `description` 참조.

## 빌드·실행

```
dotnet build
dotnet test                      # Core 규칙 테스트 — 커밋 전 필수 통과
dotnet run --project src/HfeLayoutSim.App
```

## 개발 순서

docs/ROADMAP.md의 Phase 1→5 순서를 따른다. 각 Phase 완료 시 체크리스트를 갱신할 것.
