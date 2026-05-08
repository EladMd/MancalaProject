# Mancala (Kalah) — Adversarial Greedy AI · WPF · MVVM

> פרויקט גמר הנדסת תוכנה — כיתה י"ג, שאלון 714917 (משרד החינוך).
> נכתב על ידי **אלעד**. מסמך זה משמש גם כתיעוד פרויקט וגם כיומן פיתוח מלא של השיחה שעיצבה אותו.

---

## תוכן עניינים

1. [רקע ומגבלות](#רקע-ומגבלות)
2. [ארכיטקטורה — 3 פרויקטים](#ארכיטקטורה--3-פרויקטים)
3. [מבנה קבצים](#מבנה-קבצים)
4. [התנהגות המשחק](#התנהגות-המשחק)
5. [אלגוריתם Adversarial Greedy Search](#אלגוריתם-adversarial-greedy-search)
6. [פירוט הקבועים של ההיוריסטיקה](#פירוט-הקבועים-של-ההיוריסטיקה)
7. [תור המחשב האסינכרוני](#תור-המחשב-האסינכרוני)
8. [פס הסטטוס וההודעות](#פס-הסטטוס-וההודעות)
9. [יומן פיתוח — שלבי הבנייה](#יומן-פיתוח--שלבי-הבנייה)
10. [באגים שתפסנו ותיקנו](#באגים-שתפסנו-ותיקנו)
11. [החלטות עיצוב שצריכות לדעת להגן עליהן](#החלטות-עיצוב-שצריכות-לדעת-להגן-עליהן)
12. [סקירה מול קריטריוני המחוון](#סקירה-מול-קריטריוני-המחוון)
13. [פעולות פתוחות](#פעולות-פתוחות)

---

## רקע ומגבלות

### דרישות הקריטריון
- **פרויקט הכרעת מצבים** עם בעיה אלגוריתמית (NP או חסרת קריטריון ברור).
- **אסור** לעשות שימוש ב-**Minimax**, **Alpha-Beta Pruning**, או **Monte Carlo (MCTS)**.
- **אסור** להשתמש בספריות מוכנות שמממשות את הליבה האלגוריתמית.
- שפה: C# בלבד; סביבה: Visual Studio; מסגרת: .NET 8.0.
- **חובה**: הפרדת שכבות (MVC / MVP / **MVVM**).
- ולידציות על כל הקלט.
- **אסור**: `break;` או `continue;` (ירידת 10% לכל מופע); קינון `if` עמוק (ירידת 15%).

### הבחירה האלגוריתמית
**Adversarial Greedy Search** — לא Minimax אלא חיפוש חמדני עם:
1. **סימולציית שרשרת** של תורות-נוספים.
2. **מודל אדברסרי**: היריב מומדל כשחקן חמדני קבוע (לא worst-case אדברסרי).
3. **היוריסטיקה משולבת** של 3 רכיבים: ΔS (ניקוד), C (פוטנציאל תפיסה), D (שליטה בלוח).
4. **מעבר ל-rollout דטרמיניסטי** בשלב הסיום (מעט אבנים).

---

## ארכיטקטורה — 3 פרויקטים

```
MancalaProject.sln
├── MancalaProject.Core       (Class Library, .NET 8.0)        ← Model
├── MancalaProject.Wpf        (WPF App, .NET 8.0-windows)      ← View + ViewModel
└── MancalaProject.Console    (Console App, .NET 8.0)          ← View חלופי לרגרסיה
```

### למה 3 פרויקטים?

**הסיבה אינה אסתטיקה** — היא ש-`Core` הוא Class Library שאינו תלוי ב-WPF. אם הוא ינסה להפנות ל-WPF הוא **לא יקומפל**. זאת הוכחה מהדר-לב של **הפרדת שכבות נאכפת על ידי המהדר** — קריטריון 18 בנוהל. אין דרך "לשבור" את ה-MVVM בטעות.

### הפניות בין הפרויקטים
- `Wpf` → `Core` ✓
- `Console` → `Core` ✓
- `Core` → אף אחד ❌ (זה הקסם)

---

## מבנה קבצים

```
MancalaProject.Core/
├── GameEngine.cs                 # מנוע המשחק (לוח, חוקים, מהלכים, אירועים)
├── GreedyAgent.cs                # הסוכן היריבני (Adversarial Greedy Search)
├── (Player enum)                 # Player1 / Player2
├── (Difficulty enum)             # Easy / Medium / Hard
└── (MoveResult record)           # תוצאה של ApplyMove

MancalaProject.Wpf/
├── ViewModels/
│   ├── ObservableObject.cs       # בסיס INPC
│   ├── RelayCommand.cs           # ICommand גנרי + לא-גנרי
│   ├── GameViewModel.cs          # VM של חלון המשחק
│   └── SetupViewModel.cs         # VM של חלון הפתיחה
├── Views/
│   ├── SetupWindow.xaml          # XAML חלון פתיחה
│   └── SetupWindow.xaml.cs       # code-behind מינימלי
├── App.xaml                      # ריק (ראה "באגים שתפסנו")
├── App.xaml.cs                   # OnStartup → SetupWindow → MainWindow
├── MainWindow.xaml               # XAML חלון משחק (כולל PitButtonStyle ב-Window.Resources)
└── MainWindow.xaml.cs            # נרשם לאירוע GameOver של ה-VM

MancalaProject.Console/
└── Program.cs                    # תפריט בחירה + לולאת משחק טקסטואלית
```

---

## התנהגות המשחק

### חוקים בסיסיים
- 14 בורות: 12 בורות רגילים (6 לכל שחקן) + 2 מאגרים (אחד לכל שחקן).
- כל בור מתחיל עם 4 אבנים.
- שחקן בוחר בור משלו, לוקח את כל האבנים, וזורע אחת בכל בור בכיוון השעון, **דילוג על המאגר של היריב**.
- **תפיסה**: אם האבן האחרונה נחתה בבור ריק שלי, אני תופס את אבני הבור שמולי (אצל היריב) ושם הכל במאגר שלי.
- **תור נוסף**: אם האבן האחרונה נחתה במאגר שלי, אני משחק שוב.
- **סיום**: כשלצד אחד אין אבנים בבורות הרגילים. כל האבנים בצד השני זורמות למאגר שלו (`FinalizeBoard`). מי שיש לו יותר במאגר ניצח.

### תפיסה ותור-נוסף = מצבים בלעדיים
פיזית, האבן האחרונה נופלת **או במאגר** (תור נוסף) **או בבור רגיל** (אולי תפיסה). שניהם על אותה אבן — בלתי אפשרי. הקוד ב-`UpdateStatusAfterMove` בנוי במפורש לפי ההנחה הזאת.

---

## אלגוריתם Adversarial Greedy Search

### מבנה הזרימה (per-move)
```
ApplyMove(...)
  ↓
SimulateChain(...)              ← מסמלץ את כל התורות-נוספים שלי
  ↓
SimulateOpponentGreedyResponse(...)   ← היריב משחק חמדני (לא worst-case)
  ↓
ComputeHeuristic(...)            ← h(n) = w1·ΔS + w2·C + w3·D
  +
ComputeStarvationPenalty(...)    ← עונש "האכלת" יריב מורעב
  +
GetDifficultyNoise()             ← רעש רנדומלי (לפי דרגת הקושי)
  =
Score של המהלך
```

### מעבר ל-Endgame Rollout
כשמספר האבנים על הלוח (לא במאגרים) ≤ 12, הסוכן עובר ל-`SimulateEndgameRollout`: משחק את המשחק קדימה עד הסוף עם החלטות חמדניות מיידיות (גם שלי וגם של היריב), ומחזיר את ההפרש הסופי.

זאת **אינה minimax**: זאת סימולציה אחת קדימה לכל מהלך מועמד. אין שכבות max/min מתחלפות, אין α/β.

### "מודל יריב" שאינו worst-case
הנקודה הקריטית להבנה: ב-Minimax היריב מומדל כמי שמשחק *הכי גרוע בשבילי*. ב-Adversarial Greedy Search היריב מומדל כמי שמשחק **חמדני בשביל עצמו** — עם ההיוריסטיקה שלו, לא איתי. זאת **אסטרטגיה לא-אופטימלית** (היריב יכול להיות חכם יותר בפועל), אבל היא לא דורשת lookahead של עץ מלא.

---

## פירוט הקבועים של ההיוריסטיקה

כל קבוע מתועד בתוך `GreedyAgent.cs` עם `<summary>` ו-`<remarks>` שמסביר את הנימוק.

### 3 משקלי הליבה — היררכיית ודאות
- **W1 = 1.0** — משקל ΔS (הפרש ניקוד). העוגן של ההיוריסטיקה. אבן במאגר = יחידה אחת. כל קבוע אחר נמדד יחסית לזה.
- **W2 = 0.6** — משקל פוטנציאל תפיסה. *פחות* מ-W1 כי הזדמנות היא רק היפותטית; *יותר* מ-0.5 כי תפיסה היא הכלי הטקטי הכי חזק במשחק.
- **W3 = 0.3** — משקל שליטת לוח (סכום אבנים אצלי). הקטן ביותר כי הוא רק רמז מיקומי. מותאם דינמית לפי פער הניקוד.

### Endgame
- **EndgameThreshold = 12** — רבע מ-48 האבנים שהמשחק מתחיל איתם. ברף הזה ה-branching factor כבר זול מספיק ל-rollout מלא.

### רעש לפי דרגת קושי
- **EasyNoiseRange = 3.0** — *גדול* מ-W1+W2 כדי שיוכל לעקוף החלטות חמדניות ברורות → "טעויות שחקן מתחיל".
- **MediumNoiseRange = 1.0** — שווה ל-W1. ערך החלטות סמוכות יכול להתהפך אבל לא העדפות חזקות.

### בונוסים מיידיים
- **ExtraTurnScoreBonus = 5.0** — מבוסס על 3 תועלות מצטברות: עוד מהלך (+1-2), שמירת tempo, אפשרות לשרשרת.
- **CapturePerStoneBonus = 2.0** — כל אבן נתפסת = swing של 2 (+1 לי, –1 ליריב).

### תפיסה אקטיבית/פסיבית
- **PassiveCaptureWeight = 0.5** — חצי-אמון: ההזדמנות קיימת אבל היריב יכול לחסום בתור הבא.
- **ActiveCaptureWeight = 1.0** — אמון מלא: אני יכול לבצע את התפיסה כבר עכשיו.

> **הערה על `ActiveCapturePotential`**: הבונוס מחושב פעם אחת לכל בור-יעד ריק (דרך `Any`), לא לכל בור-מקור שיכול להגיע אליו. הסיבה: בכל תור משחקים מהלך אחד בלבד, אז ריבוי בורות-מקור הם **אפשרויות מוציאות זו את זו**, לא ערך מצטבר. בנוסף, רוב מהלכי-החסימה של היריב (ריקון הבור שמולי, מילוי הבור הריק שלי) חוסמים את כל הנתיבים בו-זמנית, אז הערך השולי של נתיב נוסף נמוך משמעותית מהראשון — ובוודאי לא ליניארי. נוסחת `Sum` קודמת ניפחה את הציון פי 1–6 בהתאם למספר הנתיבים ותוקנה.

### W3 דינמי
- **LargeScoreGap = 8** — שליש מ-24 (אבנים פר שחקן). מעבר לסף הזה האסטרטגיה מתהפכת.
- **TrailingBigW3Multiplier = 1.5** — בפיגור גדול: 50% יותר משקל לאיסוף → אגרסיביות. בלידינג גדול: W3 הופך שלילי → רידוף לסיום.

### Starvation
- **StarvationOpponentStoneThreshold = 3** — הכי קטן שעדיין יכול לזרוע מהלך מאיים.
- **StarvationPenaltyPerStone = 3.0** — *גובר על W1*: גם מהלך עם +2 ניקוד שמאכיל יריב מורעב יקבל ציון שלילי כללי.

---

## תור המחשב האסינכרוני

### למה לא Thread.Sleep?
ב-WPF, `Thread.Sleep` על ה-UI thread **מקפיא** את החלון: אי אפשר לגרור, ללחוץ, לראות עדכונים. הסוכן עצמו (`GreedyAgent.ExecuteMove`) **לא** מבצע sleep — שכבת ה-VM אחראית על תזמון.

### הזרימה ב-`MaybeRunComputerTurnAsync`
```
1. בדיקה מקדימה: אם זה לא תור המחשב — return מיד.
2. capture של CancellationToken מהמשחק הנוכחי.
3. await Task.Delay(MoveResultDisplayMs)        ← 1.1s לראות את הודעת המהלך של האנושי
4. while (תור המחשב + לא נגמר):
   - StatusMessage = "Computer is thinking..." (או "thinking again..." בתור-נוסף)
   - await Task.Delay(2.5s)                      ← הדמיית "חשיבה"
   - move = await Task.Run(CalculateMove)        ← חישוב על thread pool, לא על UI
   - ct.ThrowIfCancellationRequested()           ← בדיקה אחרי ה-Run
   - ApplyMove + UpdateStatus
   - אם תור-נוסף → await Task.Delay(1.1s)        ← לראות את הודעת התוצאה
5. catch OperationCanceledException → return שקט
6. IsComputerThinking = false
```

### CancellationToken — למה הוא קיים
**הבעיה:** המשתמש לוחץ "New Game" באמצע 2.5 שניות החשיבה. ה-task הקיים ימשיך, יקרא ל-`_engine.ApplyMove(move)` — אבל `_engine` הוא כבר משחק חדש (אחרי איפוס). תוצאה: קריסה או דיפ-באג.

**הפתרון:** `_gameCts` (CancellationTokenSource) פר-משחק. ב-`StartNewGame`:
```csharp
_gameCts?.Cancel();                     // ביטול ה-task הישן
_gameCts = new CancellationTokenSource();   // token חדש למשחק החדש
```
ה-`Task.Delay(..., ct)` זורק `OperationCanceledException` בעקבות הביטול → ה-`catch` לוכד שקט → ה-task הישן מתחסל לפני שהוא נוגע ב-engine החדש.

---

## פס הסטטוס וההודעות

### כל מצב מקבל הודעה

| מצב | הודעה |
|---|---|
| משחק חדש | "Player 1's turn" |
| מהלך רגיל | "Player 2's turn" / "Computer's turn" / "Player 1's turn" |
| תפיסה | "Player X captured N stones! Y's turn" |
| תור נוסף | "Player X gets an extra turn!" |
| מחשב חושב | "Computer is thinking..." |
| מחשב חושב (תור נוסף) | "Computer got an extra turn — thinking again..." |
| סיום משחק | "Game over — Player X wins!" / "Computer wins!" / "it's a tie" |

### מדוע לא "תפיסה + תור נוסף" באותו משפט
חוקי המשחק מבטיחים שאין סיטואציה כזו. האבן האחרונה נופלת או במאגר או בבור רגיל — לא שניהם. הקוד ב-`UpdateStatusAfterMove` משקף זאת עם `if/else if/else` ולא שרשור עיוור.

### מדוע פסקי 1.1 שניות בין הודעות
**הבעיה שתפסנו:** המשתמש משחק → status = "Player 1 captured 4 stones! Computer's turn" → `MaybeRunComputerTurnAsync` רץ סינכרונית עד ה-await הראשון → עדכן status = "Computer is thinking..." **לפני שה-UI הספיק לרנדר**. המשתמש לא ראה את ההודעה.

**הפתרון:** `await Task.Delay(MoveResultDisplayMs)` בכניסה ל-`MaybeRunComputerTurnAsync`. השליטה חוזרת ל-UI thread, מתבצע render, ורק אחרי 1.1s ההודעה החדשה דורסת.

---

## יומן פיתוח — שלבי הבנייה

הפרויקט נבנה בשלבים מסודרים. כל שלב נסגר עם בילד נקי לפני המעבר הבא.

### Phase A — שחזור ל-3 פרויקטים
מצב התחלתי: פרויקט אחד שמכיל גם לוגיקה וגם UI של קונסול. הופצל ל-Core (Class Library) + Console + (בהמשך) Wpf.

### Phase B — הסרת Thread.Sleep מהסוכן
ה-Agent היה קורא ל-`Thread.Sleep(2500)` בתוך `ExecuteMove`. זה היה תקין ל-Console אבל קטסטרופלי ל-WPF. הוסר. ה-Console העביר את ה-sleep אליו עצמו.

### Phase C — שלד WPF סטטי
חלון עם מספרים קשיחים ("4" בכל בור, "0" במאגרים), `PitButtonStyle` עם hover/pressed/disabled.

### Phase D — ViewModel + Data Binding
נוצרו `ObservableObject`, `RelayCommand`, `RelayCommand<T>`, `GameViewModel`. המספרים הקשיחים הוחלפו ב-`{Binding PitN}` וכו'.

### Phase E — קלט שחקן
חיבור 12 הכפתורים ל-`PlayPitCommand` עם `CommandParameter` של אינדקס מוחלט. כפתורים לא-חוקיים מתעמעמים אוטומטית בזכות `CanExecute` של ה-Command.

### Phase F — תור מחשב אסינכרוני
מימוש `MaybeRunComputerTurnAsync` עם `Task.Delay` ו-`Task.Run`.

### Phase G — סיום משחק + New Game
- כפתור "New Game" בכותרת (דרך `HeaderButtonStyle`).
- אירוע `GameOver` ב-VM ש-`MainWindow.xaml.cs` נרשם אליו ומציג `MessageBox`.
- `CancellationToken` כדי שלחיצה על New Game באמצע תור המחשב לא תקרוס.

### Phase H — מסך Setup
חלון פתיחה עם בחירת מצב (PvP/PvE) ודרגת קושי (Easy/Medium/Hard). `App.xaml.cs` מוריד את ה-`StartupUri`, מציג Setup כדיאלוג, ואז בונה `MainWindow` עם הבחירות.

### Phase I — הדגשת שחקן פעיל (נוסף ואז הוסר)
ניסינו להוסיף מסגרת זהובה סביב השורה הפעילה. **הוסר על פי בקשת המשתמש** כי ה-disabled-state של ה-`PitButtonStyle` כבר נותן רמז ויזואלי חזק (כפתורי היריב אפורים עם 60% opacity), וה-status bar אומר את התור במפורש. הוספה הייתה over-engineering.

---

## באגים שתפסנו ותיקנו

### 1. `<!-- ----- text ----- -->` ב-XAML
XML לא מאפשר `--` בתוך הערה. הקיים `-----` היה גורם ל-`MC3000` בקומפיילציה. תוקן.

### 2. `GetWinner() called before FinalizeBoard()`
ב-VM קראנו ל-`BuildGameOverMessage` → `GetWinner` בלי לקרוא לפני זה ל-`FinalizeBoard`. הוסף קריאה.

### 3. `Cannot find resource named 'PitButtonStyle'`
לאחר הסרת `StartupUri` מ-App.xaml, מערכת הבילד של WPF הפסיקה לייצר `App.baml`. כל הסטיילים שהיו ב-`Application.Resources` נעלמו בזמן ריצה. **הפתרון**: העברת ה-`PitButtonStyle` ל-`MainWindow.xaml` תחת `Window.Resources` — Window.baml תמיד נוצר.

### 4. ShutdownMode race ב-OnStartup
ברירת המחדל של `ShutdownMode` היא `OnLastWindowClose`. כשה-Setup נסגר, היה רגע של "0 חלונות פתוחים" → WPF החל סגירה אוטומטית. ה-MainWindow נפתח לרגע אבל הסגירה כבר התחילה. **הפתרון**: `ShutdownMode = OnExplicitShutdown` בכניסה ל-OnStartup, וחזרה ל-`OnLastWindowClose` אחרי ש-MainWindow מוצג.

### 5. הודעות סטטוס נדרסות לפני render
`OnPlayPit` קבע status → `_ = MaybeRunComputerTurnAsync()` רץ סינכרונית עד ה-await הראשון → קבע status חדש לפני שה-UI עשה render. **הפתרון**: `await Task.Delay(1.1s)` בכניסה ל-`MaybeRunComputerTurnAsync`.

### 6. תפיסה ותור נוסף שורשרו "כאילו אפשרי"
הקוד הקודם בנה את ההודעה משני חלקים נפרדים שכל אחד מהם נבדק עצמאית. בתיאוריה היה יוצר "X captured N! X gets extra turn!" — בלתי אפשרי לפי חוקי המשחק. נכתב מחדש עם `if/else if/else` למצבים בלעדיים.

### 7. SizeToContent על SetupWindow
הגובה הקבוע (`Height=500`) חתך את הכפתורים. הוחלף ב-`SizeToContent="Height"` עם הסרת שורת ה-spacer (`<RowDefinition Height="*"/>`) שגזלה מקום מהכפתורים כשהחלון לא היה גבוה מספיק.

---

## החלטות עיצוב שצריכות לדעת להגן עליהן

### למה Adversarial Greedy ולא Minimax?
- **קריטריון**: Minimax אסור בנוהל (סעיף 13).
- **תוכן**: Adversarial Greedy אינו עץ מלא — הוא 1-ply שלי + 1-ply של יריב חמדני + היוריסטיקה. אין שכבות max/min, אין α/β.
- **מדוע "הכרעת מצבים" עדיין תקפה**: כי אין קריטריון ברור לבחירת המהלך הבא בלי חישוב היוריסטי, ואין פתרון פולינומיאלי ידוע למצב סוף שונה מ-rollout.

### למה MVVM ולא MVC?
WPF בנוי על MVVM (Data Binding נאטיבי). כפיית MVC על WPF דורשת קוד נוסף בלי תועלת. הנוהל מאפשר את שלושת הסגנונות, אז MVVM הוא הבחירה הטבעית.

### למה הסוכן ניטרלי לזהות השחקן?
`GreedyAgent` מקבל `Player` בקונסטרקטור. אותה מחלקה יכולה לשחק כ-Player1 או Player2 לפי הצורך. זה מאפשר סימולציות "מה היה קורה אם" עם החלפת תפקידים.

### למה אין `INotifyPropertyChanged` ב-Engine?
ל-Engine יש `event Action? BoardChanged` — אירוע פשוט בלי תלות ב-WPF. ה-VM נרשם אליו ומתרגם ל-`PropertyChanged`. אם נכניס INPC ל-Engine, הוא יקבל תלות ב-`System.ComponentModel` שעדיין לא בעייתית, אבל זה ריח של זיהום אדריכלי. עדיף שכבת תרגום.

### למה event ולא callback ל-MessageBox?
ה-VM **לא מכיר** את `MessageBox`. אם הוא יקרא ישירות, הוא יזדהם ב-WPF (`System.Windows`). במקום זה, ה-VM מפיץ אירוע `GameOver(string)` וה-View נרשם. אם תחליף את ה-View מחר ל-WinForms או Avalonia — ה-VM לא צריך להשתנות.

---

## סקירה מול קריטריוני המחוון

(תוצאות הסקירה האחרונה — כל הסעיפים PASS)

| קריטריון | סטטוס | הערות |
|---|---|---|
| אלגוריתמים אסורים (Minimax/AB/MCTS) | ✅ | אפס מופעים. תיעוד מפורש שלא משתמשים. |
| `break;` / `continue;` (-10% כל מופע) | ✅ | אפס מופעים. |
| קינון `if` עמוק (-15%) | ✅ | פונקציות קצרות, early returns, ternaries. |
| העדר תיעוד (-10%) | ✅ | XML doc מלא על כל public API. |
| MVVM / הפרדת שכבות | ✅ | Core בלי שום הפניה ל-WPF. |
| ספריות מוכנות | ✅ | רק System.Linq (מותר). |
| ולידציות | ✅ | `ApplyMove` → `ValidateMove`; agent מוגבל ל-`GetValidMoves`. |
| DRY / קוד גנרי | ✅ | קבועים במקום אחד, helpers משותפים. |
| שפה וסביבה | ✅ | C# + .NET 8 + Visual Studio. |

---

## פעולות פתוחות

### חיוני לפני הבחינה
1. **ספר הפרויקט** (25% מהציון). 23 סעיפים מוגדרים בנוהל. נדרשים כולל UML, פסאודו-קוד, רפלקציה אישית, ביבליוגרפיה ב-APA.
2. **התאמה להצעה המאושרת**. לוודא שמה שבנינו (PvE, WPF, מצב Setup) **מופיע** או לפחות **לא סותר** את ההצעה שאושרה.

### אופציונלי בקוד
- Unit tests ל-`GameEngine` (לא נדרש לפי הנוהל אבל מעלה ביטחון בבחינה).
- שינוי שם של `TryCapture` בתוך Engine למשהו ברור יותר אם קיים.
- לוגינג בסיסי (לא קריטי).

### לבחינה (תרגול דיבור)
- הסבר על W1, W2, W3 בעל פה.
- הסבר על למה Mancala הוא "הכרעת מצבים".
- הסבר על MVVM ולמה 3 פרויקטים.
- הסבר על `CancellationToken` — מה הבעיה שהוא פותר.
- הסבר על `Task.Run` ב-`CalculateMove` — למה לא לרוץ על UI thread.

---

## הפעלה

```bash
cd D:\Users\ELAD\MancalaProject
dotnet build MancalaProject.sln
```

הפעלת ה-WPF:
- ב-Visual Studio: לחיצה ימנית על `MancalaProject.Wpf` → Set as Startup Project → F5.

הפעלת הקונסול:
```bash
dotnet run --project MancalaProject.Console
```

---

*מסמך זה משקף את מצב הפרויקט נכון לסוף שיחת הפיתוח. לכל שאלה שאלעד יקבל בבחינה — התשובה צריכה להיות עקבית עם המסמך הזה.*
