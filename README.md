# HFE Layout Simulator — 검사 레이아웃 설계·평가 시뮬레이터

의료기기 QC의 **검사 문서(A4 종이 양식)와 MES 화면(1920×1080) 레이아웃**을 설계하고,
인지공학(HFE) 규칙 지식베이스로 자동 채점·분석하는 도구.
사내 매뉴얼 MNL-QA-HFE-002를 기계화한 9개 카테고리·51개 규칙
(IEC 62366-1, AAMI HE75, ISO 9241-110, WCAG 2.2 AA, Nielsen 휴리스틱 근거)으로
레이아웃의 강점/취약점을 [규칙 ID + 실측 vs 기준 + 해당 요소 + 개선 권고 + 근거 표준]
형식으로 리포트한다.

## 현재 상태

- ✅ **Phase 1** — Core 도메인 모델(Layout/Semantics), 규칙 로더, WCAG 대비율·단위 변환 유틸
- ✅ **Phase 2** — 평가 엔진(HfeEngine): C1~C9 전 규칙 evaluator, 채점(가중 총점·등급·Critical 상한),
  콘솔 러너, 단위 테스트 113건 통과
- ⏳ **Phase 3~5** — WPF 편집기·리포트 UI·비교 뷰 (Windows 환경 필요, `docs/ROADMAP.md` 참조)

## 빌드·실행 (.NET 8 SDK)

```bash
dotnet build
dotnet test                      # Core 규칙 테스트 — 커밋 전 필수 통과

# 레이아웃 평가 (콘솔)
dotnet run --project src/HfeLayoutSim.Cli -- evaluate samples/incoming_inspection_paper.hfelayout.json
dotnet run --project src/HfeLayoutSim.Cli -- evaluate samples/bad_layout_paper.hfelayout.json
```

`samples/`에는 모범 레이아웃 2종(Paper 성적서 A등급, MES Screen A등급)과
의도적 불량 레이아웃 1종(16개 규칙 위반 검출 시연)이 있다.

## 구조

```
src/HfeLayoutSim.Core/    도메인 모델 + 평가 엔진 (UI 의존성 0)
src/HfeLayoutSim.Cli/     콘솔 러너 (배치 평가)
tests/                    xUnit 테스트 (규칙별 위반/통과/N.A. 케이스)
rules/hfe_rules.json      ★ 채점 규칙 지식베이스 — 임계값·가중치·메시지의 단일 진실 원천
docs/SPEC.md              상세 요구사항 명세
docs/ROADMAP.md           단계별 개발 계획·진행 현황
docs/DESIGN_TOKENS.md     검증된 색상·크기 기준
```

규칙 수치는 코드에 하드코딩하지 않는다 — `rules/hfe_rules.json` 편집만으로 조정 가능하다.
