# SPEC — HFE Layout Simulator 상세 요구사항 명세

버전 1.0 · 근거: MNL-QA-HFE-002 「HFE 문서 및 UI/UX 설계 매뉴얼」

---

## 1. 목적

검사 양식(종이 A4)과 MES 검사 화면(1920×1080)의 레이아웃을 시각적으로 구성하고,
HFE 규칙 엔진으로 **정량 채점 + 정성 분석 리포트**를 생성하여, 배치안 간 비교·개선
반복(iteration)을 지원한다. 이는 IEC 62366-1의 형성 평가(Formative Evaluation)를
설계 단계에서 보조하는 도구다.

## 2. 사용자 시나리오 (핵심 3개)

- **S1 설계**: 새 레이아웃 생성(매체 선택: Paper/Screen) → 팔레트에서 요소 드래그 배치
  → 속성 패널에서 스타일·의미(Semantics) 지정 → 저장(JSON).
- **S2 평가**: [평가 실행] → 9개 카테고리 점수(0–100)+총점, Finding 목록
  (심각도순 정렬, 캔버스에서 해당 요소 하이라이트 연동) → 리포트 저장/인쇄(HTML/PDF).
- **S3 비교**: 동일 양식의 Variant A/B/C를 나란히 열고 카테고리별 레이더 차트 +
  Finding diff("A에서 해소됨/B에서 신규 발생") 표시.

## 3. 캔버스 사양

| 항목 | Paper | Screen |
|---|---|---|
| 크기 | A4 210×297mm (가로 모드 지원) | 1920×1080px (1366×768 미리보기 토글) |
| 단위 | mm (소수 1자리) | px (정수) |
| 그리드 | 1mm 스냅, 5mm 보조선 | 8px 스냅 (8px 그리드 규칙) |
| 여백 가이드 | 상15/하15/좌20/우12mm 가이드라인 표시 | 콘텐츠 패딩 24px 가이드 |
| 배율 | 50–400% 줌, Fit | 동일 |
| 구역 오버레이 | 구텐베르크 4분면(POA/SFA/WFA/TA) 토글 표시 | 동일 + 고정영역(헤더56/GNB240/컨텍스트48/액션바64) 가이드 |

## 4. 요소(Element) 카탈로그

공통 속성: `Id, Type, X, Y, W, H, Z, Rotation(0/90)`,
`Style { FontFamily, FontSizePt(paper)/Px(screen), Bold, Italic, FgColor, BgColor, BorderColor, BorderWidth, Align }`,
`Semantics`(§5), `Text`(표시 문구), `GroupId`(소속 그룹).

| Type | 설명 | 기본 크기(Paper/Screen) |
|---|---|---|
| Header | 문서/화면 제목 블록 | 180×12mm / 폭×56px |
| Label | 항목 라벨 | 30×5mm / 120×20px |
| TextInput | 자유 텍스트 기입란 | 50×8mm / 200×40px |
| NumInput | 측정값 기입란(단위·자릿수 속성) | 12×6mm / 110×40px |
| Checkbox | 체크박스(라벨 포함) | 5×5mm / 24×24px |
| RadioGroup | 상호배타 선택(옵션 수 속성) | 40×6mm / 200×24px |
| Table | 검사항목 표(행·열 수, 열 역할[항목/기준/측정/판정]) | 자유 / 자유 |
| JudgmentBadge | 판정 표시(적합/부적합/보류) — hasIcon/hasText/hasColor 속성 | 20×6mm / 90×24px |
| Button | (Screen 전용) Primary/Secondary/Danger/Ghost | — / 120×44px |
| SignatureBox | 서명란(서명자 역할 속성) | 50×10mm / 160×48px |
| WarningBox | 경고/주의 박스 | 자유 |
| InfoBox | 참고/게이트 문구 박스 | 자유 |
| Image | 도해·사진 자리 | 자유 |
| Barcode | 바코드/QR 표시 | 40×10mm / 160×40px |
| Section | 그룹 컨테이너(제목 속성) — 자식 요소를 담음 | 자유 |
| Divider | 구분선 | 자유 |
| Stepper | (Screen 전용) 진행 단계 표시 | — / 400×32px |

## 5. Semantics (의미 속성) — 평가의 핵심 입력

```jsonc
"semantics": {
  "role": "Identification | Precondition | SamplingInfo | InspectionItem |
           NonconformanceRecord | FinalVerdict | Signature | Warning | Reference | Navigation | Action | Decoration",
  "sequence": 12,            // 작성/입력 순서 (1부터; 0=순서 무관)
  "groupId": "dim",          // 논리 그룹
  "isCritical": true,        // 안전 관련 필드 (판정·LOT·멸균 파라미터 등) — 위반 시 심각도 1단계 상향
  "isRequired": true,
  "judgment": { "hasColor": true, "hasIcon": true, "hasText": true },  // 판정류 요소의 3중 코딩
  "isDestructiveAction": false,   // (버튼) 삭제·판정취소류
  "inputKind": "check | transcribe | auto",  // 중요 확인의 전기(轉記) 여부 판별용
  "specLimitShown": true     // 측정 입력 옆 규격 한계 병기 여부
}
```

## 6. 평가 엔진 (HfeEngine)

### 6.1 계산 유틸

- **대비율**: WCAG 2.x 상대 휘도. `L = 0.2126R'+0.7152G'+0.0722B'`,
  `c' = c/12.92 (c≤0.03928) else ((c+0.055)/1.055)^2.4`, `CR=(L1+0.05)/(L2+0.05)`.
  검증 케이스: `#212529/#FFFFFF=15.43`, `#1565C0/#FFFFFF=5.75`, `#2E7D32/#FFFFFF=5.13`,
  `#C62828/#FFFFFF=5.62`, `#B45309/#FFFFFF=5.02`, `#F57C00/#FFFFFF=2.70(실패 케이스)`,
  `#868E96/#FFFFFF=3.32`. (±0.02 허용)
- **유효 배경색 결정**: 요소 BgColor가 투명이면 Z순서상 아래 요소→캔버스 배경 순으로 탐색.
- **대형 텍스트 판정**: paper ≥18pt or (≥14pt and Bold) / screen ≥24px or (≥18.5px and Bold).
- **구역 판정**: 요소 중심점이 속한 4분면(캔버스를 2×2 분할; Screen은 고정영역 제외한
  콘텐츠 영역 기준). POA=좌상, SFA=우상, WFA=좌하, TA=우하.
- **정보 밀도(Paper)**: Σ(요소 면적, Section 제외)/인쇄 가용 면적.
  **밀도(Screen)**: 최상위 블록(Section/Table/카드) 수 및 입력 필드 수.
- **읽기 순서 일치도**: sequence 부여 요소를 순서대로 이었을 때, 다음 요소가
  이전 요소보다 (a) 아래 행이거나 (b) 같은 행에서 오른쪽이면 순방향.
  역방향 전이 횟수/전체 전이 = 역류율.
- **그룹 간격 분석**: 동일 groupId 요소 쌍의 최근접 간격 중앙값(intra) vs
  인접 그룹 간 최근접 간격(inter). inter/intra ≥ 2.5 요구.

### 6.2 평가 카테고리 (9종) — 규칙 상세는 rules/hfe_rules.json

| ID | 카테고리 | 대표 규칙 (발췌) | 근거 |
|---|---|---|---|
| C1 | 색상·대비 | 텍스트 4.5:1(대형 3:1), 비텍스트 3:1, 판정 3중코딩, 의미색 면적≤10%, 적록 인접 구분 금지 | WCAG 1.4.x, HE75 |
| C2 | 타이포그래피 | 최소 크기(8pt/12px), 서체≤2종, 크기 계층≤5, 측정값 등폭, 강조수단(이탤릭 금지) | HE75, §3.1 |
| C3 | 크기·조작 | 체크박스≥5mm/24px, 버튼 높이≥40px, 타겟 간격, Danger 이격≥24px, 서명란≥50×10mm | Fitts, WCAG 2.5.8 |
| C4 | 배치·시선 흐름 | 식별정보 POA 존재, WFA에 Warning/Critical 금지, 판정·서명 TA, 헤더 상단, (Screen)주 버튼 우하단 | F-패턴/구텐베르크 |
| C5 | 그룹화·근접성 | inter/intra≥2.5, 라벨-입력 근접(≤3mm/8px), 그룹 크기 3–7, 연속 동일 체크박스≤4 | 게슈탈트 |
| C6 | 인지 부하·밀도 | 밀도 40–60%(경고 60–75, 실패>75), 입력 필드≤15/25, 표 열≤7/9, 그룹 미지정 항목 수 | Sweller, Miller |
| C7 | 순서·업무 흐름 | 역류율=0, Precondition이 InspectionItem보다 선행, 완결성 게이트가 Verdict 직전, 부적합기록 블록 존재, 8블록 순서 | ISO 9241-110 ①③, 62366-1 |
| C8 | 오류 방지·강건성 | Critical 확인의 전기(transcribe) 방식, 측정 입력의 규격 병기(specLimitShown), (Screen) Danger 확인 경유·필수 표시(*), 판정 요소 존재 | H5, WCAG 3.3.x |
| C9 | 경계·부하 관리 | 반복 측정 표본>20이면 구간 분할 존재, 페이지/스텝 분할, (Paper) Page n/N 존재 | Vigilance, §4 |

### 6.3 채점 방식

- 규칙별 결과: `Pass | Violation(severity: Minor=–3, Major=–8, Critical=–20) | NotApplicable`.
- `isCritical` 요소에서의 위반은 심각도 1단계 상향.
- 카테고리 점수 = `max(0, 100 – Σ penalty)` (해당 카테고리 규칙만).
- **총점 = Σ(카테고리 점수 × 가중치)** — 가중치는 rules JSON `weights` (기본:
  C1 .12, C2 .08, C3 .10, C4 .13, C5 .10, C6 .12, C7 .15, C8 .15, C9 .05).
- 등급: A≥90 / B≥80 / C≥70 / D≥60 / F<60. **Critical 위반이 1건이라도 있으면 등급 상한 C**
  (배포 부적합 표시) — 매뉴얼 §7.2 판정 규칙 반영.

### 6.4 리포트 (EvaluationReport)

```
ScoreCard { total, grade, categories[9]{score, passCount, violationCount} }
Findings[] {
  ruleId, category, severity, title,
  measured: "12개 연속 체크박스",  target: "≤ 4",
  elementIds: [...],              // 캔버스 하이라이트 연동
  recommendation: "3–4개 단위로 그룹화하고 일부를 기입란으로 교체…",
  standardRef: "게슈탈트 유사성 / MNL-QA-HFE-002 §3.5.2"
}
Strengths[]  // Pass 중 주목할 규칙 (rules JSON에 highlightOnPass=true 표시된 것)
```

리포트 뷰 요구: 심각도 필터, 카테고리 필터, Finding 클릭→캔버스 해당 요소로 스크롤+점멸
(점멸은 3회 후 고정 — 광과민성), HTML 내보내기(자체 템플릿), 인쇄.

### 6.5 비교(Compare) 뷰

- 최대 3개 Variant 병렬: 미니 캔버스 썸네일 + 카테고리 레이더 차트 겹침 + 총점 막대.
- Finding diff: ruleId 기준 집합 비교 → "해소됨(A→B)", "신규 발생", "공통 잔존".

## 7. 파일 포맷

- 레이아웃: `*.hfelayout.json` — `{ meta{name, medium, variantOf, createdAt}, canvas{}, elements[] }`
- 리포트: `*.hfereport.json` + HTML 내보내기.
- 규칙: `rules/hfe_rules.json` — 스키마는 파일 상단 `$schemaNote` 참조. 앱 시작 시 로드,
  파싱 실패 시 명확한 오류(줄번호)와 함께 기동 중단.

## 8. UI 구성 (앱 자체도 매뉴얼 표준을 따를 것)

- 좌: 요소 팔레트(카테고리 그룹) / 중: 캔버스(탭=열린 레이아웃) / 우: 속성 패널(선택 요소) ↔ 평가 리포트 패널 전환.
- 상단 툴바: 새로 만들기(매체 선택), 열기/저장, Variant 복제, 구역 오버레이 토글, [평가 실행](Primary, 우측 배치).
- 템플릿: "8블록 표준 성적서(Paper)", "MES 검사수행 화면(Screen)" 프리셋 제공 — 신규 생성 시 선택 가능.
- 앱 팔레트·버튼·배지는 DESIGN_TOKENS.md의 검증 색상 사용. 앱 스스로가 규칙 위반이면 안 됨.

## 9. 비기능 요구

- 요소 500개 레이아웃 평가 < 1초. 평가는 비동기 실행(UI 블로킹 금지).
- Undo/Redo ≥ 50단계 (요소 이동·속성 변경 모두).
- 자동 저장(30초, 임시 파일). 다국어는 고려하지 않음(한국어 고정).
