# HFE Layout Simulator — 검사 레이아웃 설계·평가 시뮬레이터

의료기기 QC의 **검사 문서(A4 종이 양식)와 MES 화면(1920×1080) 레이아웃**을 설계하고,
인지공학(HFE) 규칙 지식베이스로 자동 채점·분석하는 도구.
사내 매뉴얼 MNL-QA-HFE-002를 기계화한 9개 카테고리·58개 규칙
(IEC 62366-1, AAMI HE75, ISO 9241-110, WCAG 2.2 AA, Nielsen 휴리스틱 근거)으로
레이아웃의 강점/취약점을 [규칙 ID + 실측 vs 기준 + 해당 요소 + 개선 권고 + 근거 표준]
형식으로 리포트한다.

## 현재 상태

- ✅ **Phase 1** — Core 도메인 모델(Layout/Semantics), 규칙 로더, WCAG 대비율·단위 변환 유틸
- ✅ **Phase 2** — 평가 엔진(HfeEngine): C1~C9 전 규칙 evaluator, 채점(가중 총점·등급·Critical 상한),
  콘솔 러너, 단위 테스트 통과
- ✅ **Phase 3** — WPF 편집기: 팔레트 배치·드래그·키보드 이동, 속성(기하·스타일·Semantics) 편집,
  Undo/Redo, 자동 저장·복구, 실시간 코칭 패널
- ✅ **Phase 4** — 리포트 패널: PASS/FAIL 배너·카테고리 등급·심각도/카테고리 필터·강점 목록,
  Finding 클릭 시 캔버스 스크롤+점멸, HTML 내보내기
- ✅ **Phase 5 (1차)** — Variant 비교 창(총점·카테고리 표·Finding diff)
- ✅ **지식베이스 품질** — §8.1 커버리지 매핑(`docs/RULE_COVERAGE.md`), 신설 규칙 7건(총 58개),
  규칙 튜닝 회귀 도구(`regress`)
- ✅ **설계 보조** — 요소 프리셋 라이브러리(`rules/element_presets.json`), 배치 어드바이저(`advise`:
  위치·색상·폰트·크기·아이콘·순서 코칭), 검사 카탈로그 → 레이아웃 자동 생성(`generate`)
- ✅ **Verifier식 판정** — 바코드 검증기(Zebra 2D verifier) 스타일 PASS/FAIL 배너 +
  카테고리별 등급(A~F) — CLI·HTML·WPF 리포트 공통
- ✅ **강건성(fail-proof)** — 단일 인스턴스, 전역 예외 처리, 원자적 저장, 손상 파일 복구 안내,
  규칙 파일 분실 시 내장본 대체, 평가 결과 무효화(stale) 차단, 앱 자체 HFE 준수 테스트

## 프로그램(exe) 내려받기 — 코딩·명령창 불필요

푸시할 때마다 GitHub가 Windows용 실행파일을 자동으로 만들어 둡니다.

1. 브라우저에서 저장소 페이지 → 상단 **Actions** 탭 클릭
2. 목록에서 초록 체크(✓)가 붙은 가장 최근 **"Build Windows EXE"** 실행 클릭
3. 페이지 하단 **Artifacts**에서 **HfeLayoutSim-win64** 클릭 → zip 다운로드
4. 압축을 푼 뒤 **HfeLayoutSim.exe** 더블클릭
   (Windows 보호 경고가 뜨면 "추가 정보 → 실행" 클릭. `rules` 폴더는 exe와 같은 폴더에 있어야 함 — 압축을 통째로 풀면 됨)

앱 사용 순서: **새 양식(Paper)** → 왼쪽 팔레트에서 요소 더블클릭으로 배치 → 드래그로 이동
→ 오른쪽 **코칭** 탭에서 위치·색·폰트 진단 확인 → **[평가 실행]** → **리포트** 탭에서 PASS/FAIL 확인.
**카탈로그로 생성…** 버튼으로 `catalog/*.catalog.json`(공정·항목·기준 정의)에서 완성된 양식을 바로 만들 수도 있습니다.

자세한 사용법(단축키, 카탈로그 작성법, 문제 해결)은 **[docs/USER_GUIDE.md](docs/USER_GUIDE.md)** 에 있습니다.

## 빌드·실행 (.NET 8 SDK)

```bash
dotnet build
dotnet test                      # Core 규칙 테스트 — 커밋 전 필수 통과

# 레이아웃 평가 (콘솔 + JSON/HTML 리포트)
dotnet run --project src/HfeLayoutSim.Cli -- evaluate samples/incoming_inspection_paper.hfelayout.json
dotnet run --project src/HfeLayoutSim.Cli -- evaluate samples/bad_layout_paper.hfelayout.json --html report.html

# Variant 비교 (2~3개: 총점/카테고리 점수/Finding diff)
dotnet run --project src/HfeLayoutSim.Cli -- compare samples/bad_layout_paper.hfelayout.json samples/incoming_inspection_paper.hfelayout.json

# 배치 어드바이저 — 요소별 위치/색상/폰트/크기/아이콘/순서 코칭 (elementId 생략 시 전체)
dotnet run --project src/HfeLayoutSim.Cli -- advise samples/bad_layout_paper.hfelayout.json cb-lot

# 검사 카탈로그(공정/항목/기준/이미지/알람 사용자 정의) → 표준 레이아웃 자동 생성
dotnet run --project src/HfeLayoutSim.Cli -- generate catalog/incoming_inspection.catalog.json --medium paper --out my_form.hfelayout.json

# 규칙 튜닝 회귀 — 수정 규칙 vs 기준 규칙으로 샘플 전체 재평가·차이 보고
dotnet run --project src/HfeLayoutSim.Cli -- regress /tmp/tuned_rules.json samples/*.hfelayout.json
```

`samples/`에는 모범 레이아웃 2종(Paper 성적서 A등급, MES Screen A등급)과
의도적 불량 레이아웃 1종(16개 규칙 위반 검출 시연)이 있다.

## 구조

```
src/HfeLayoutSim.Core/       도메인 모델 + 평가 엔진 + 어드바이저/생성기/비교/회귀 (UI 의존성 0)
src/HfeLayoutSim.Cli/        콘솔 러너 (evaluate/compare/advise/generate/regress)
src/HfeLayoutSim.App/        WPF 편집기 (팔레트·캔버스·코칭·리포트·비교)
tools/XamlLint/              XAML 바인딩·리소스 정적 검사 (CI에서 실행)
tests/HfeLayoutSim.Core.Tests/  규칙별 위반/통과/N.A. + 강건성 + 앱 자체 HFE 준수 (모든 OS)
tests/HfeLayoutSim.App.Tests/   WPF 실행 스모크 — 실제 창을 띄워 명령·바인딩 검증 (Windows 전용, CI)
rules/hfe_rules.json         ★ 채점 규칙 지식베이스(58규칙) — 임계값·가중치·메시지의 단일 진실 원천
rules/element_presets.json   요소 프리셋 라이브러리 — 팔레트 기본값·권장 구역·가이드 문구 (사용자 편집 가능)
catalog/                     검사 카탈로그 예시 — 공정/항목/기준/이미지/알람 사용자 정의
docs/SPEC.md                 상세 요구사항 명세
docs/ROADMAP.md              단계별 개발 계획·진행 현황
docs/RULE_COVERAGE.md        매뉴얼 §8.1 체크리스트 ↔ 규칙 커버리지 매핑·회귀 절차
docs/USER_GUIDE.md           사용 설명서(비개발자용) — exe 실행부터 카탈로그 정의·문제 해결까지
docs/DESIGN_TOKENS.md        검증된 색상·크기 기준
```

규칙 수치는 코드에 하드코딩하지 않는다 — `rules/hfe_rules.json` 편집만으로 조정 가능하다.
