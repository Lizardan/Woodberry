using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Woodberry.Editor.ArtStudio
{
    /// <summary>
    /// Окно арт-студии: очередь ревью, каталог кадров проекта, генерация.
    ///
    /// КАК УСТРОЕН ИНТЕРФЕЙС И ПОЧЕМУ ТАК. В работе два разных объекта, и
    /// раньше они выглядели одним — отсюда и путаница:
    ///
    /// <list type="bullet">
    /// <item><b>Кандидат</b> — файл в <c>ArtStudio/review/</c>, в проекте его
    /// ещё нет. Решения: «Принять в проект» (файл переносит драйвер) и
    /// «Отклонить» (кадр уходит на перегенерацию).</item>
    /// <item><b>Кадр проекта</b> — файл в <c>Assets/…</c>. Решения: «Отметить
    /// принятым» (запись в журнал ревью) и «Пометить браком» (кадр уходит на
    /// перегенерацию).</item>
    /// </list>
    ///
    /// Правила, которые держат это понятным:
    /// <list type="number">
    /// <item>Один объект — одно место решения. Действия живут только в правой
    /// панели, её заголовок называет объект, а наборы действий кандидата и
    /// кадра показываются взаимоисключающе.</item>
    /// <item>Действия стоят рядом с тем, на что действуют: «Сгенерировать» —
    /// под деревом пулов, «Доп. места» — в разделе проекта, а не вперемешку
    /// с действиями кадра.</item>
    /// <item>Кандидаты читаются с диска (<see cref="ReviewScanner"/>), а не из
    /// событий драйвера: события не переживают перезапуск и оставляют
    /// призраков.</item>
    /// </list>
    ///
    /// Почему дерево категорий — не <see cref="TreeView"/>. Групп и пулов
    /// десятки, кадров — сотни. Виртуализация нужна списку кадров, а дереву
    /// нет: обычные элементы проще, не зависят от API контроллера дерева и
    /// не ломаются при его смене. Там, где виртуализация действительно
    /// важна, стоит <see cref="ListView"/>.
    ///
    /// Что окно НЕ делает: не ходит по файловой системе на каждый символ
    /// поиска (каталог строится один раз), не лезет в <c>Assets</c> мимо
    /// <see cref="AssetDatabase"/>.
    /// </summary>
    public sealed class ArtStudioWindow : EditorWindow
    {
        private const string UxmlPath = "Assets/Woodberry/UI/Editor/ArtStudio/ArtStudio.uxml";

        // Пропорция кадра диалога, если текстура ещё не загрузилась:
        // портрет 480x720. У фона пропорция своя (856x480) и берётся из текстуры.
        private const float PortraitWidth = 480f;
        private const float PortraitHeight = 720f;

        /// <summary>
        /// Отступы и рамка правого блока: 6+6 из <c>.panel-right</c> плюс его
        /// левая рамка. Блок шире кадра ровно на это, без запаса, — числа
        /// обязаны совпадать с USS, иначе кадр перестанет заполнять блок.
        /// </summary>
        private const float RightPanelChrome = 13f;

        private Label _status;
        private Label _problems;
        private Label _framesHeader;
        private Label _detailHeader;
        private Label _frameTitle;
        private Label _frameId;
        private Label _framePath;
        private Label _frameStatus;
        private Label _frameCoverage;
        private Image _preview;
        private VisualElement _previewHolder;
        private VisualElement _rightPanel;
        private VisualElement _leftPanel;
        private VisualElement _centerPanel;
        private Button _approve;
        private Button _reject;
        private Button _openFolder;
        private Button _accept;
        private Button _rejectCandidate;
        private VisualElement _candidateActions;
        private VisualElement _frameActions;
        private VisualElement _rejectComment;
        private TextField _comment;
        private ToolbarSearchField _search;
        private ToolbarMenu _statusFilter;
        private ToolbarMenu _kindFilter;
        private ToolbarToggle _onlyRegenerable;
        private Button _refreshButton;
        private ScrollView _categories;
        private ListView _frames;
        private ToolbarButton _standButton;
        private Button _regenButton;
        private VisualElement _addRow;
        private Label _addHint;
        private IntegerField _addCount;
        private Button _addButton;
        private VisualElement _placesPanel;
        private Button _placesToggle;
        private ScrollView _placeList;
        private DropdownField _placeGroup;
        private TextField _placeId;
        private TextField _placeLabel;
        private TextField _placeScene;
        private Button _placeAdd;

        private ArtDriverClient _client;

        /// <summary>Подключение в прошлом опросе: ловим переход «нет → есть».</summary>
        private bool _wasDriverConnected;

        private readonly List<string> _loadProblems = new List<string>();
        private readonly Dictionary<string, Texture2D> _candidateTextures =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        /// <summary>
        /// Кандидаты читаются с диска: папка <c>review/</c> — источник правды,
        /// события драйвера только повод пересканировать (см. ReviewScanner).
        /// </summary>
        private readonly List<ReviewCandidate> _reviewCandidates = new List<ReviewCandidate>();
        private readonly List<string> _reviewProblems = new List<string>();

        /// <summary>Подпись левой панели: пересобираем только при изменении.</summary>
        private string _leftSignature;

        /// <summary>Подпись очереди ревью: у неё своя, отдельная от дерева.</summary>
        private string _reviewSignature;

        /// <summary>
        /// Свёрнутые разделы дерева. Нужны потому, что панель пересобирается
        /// целиком, а раскрытость — состояние человека, а не модели: без
        /// памяти каждая пересборка раскрывала бы все разделы заново.
        /// </summary>
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);

        private string _projectRoot;
        private CatalogModel _model;
        private FeedbackStore _feedback;
        private ExtraPoolsFile _extraPools;
        private ArtSettings _settings;

        /// <summary>
        /// Отказы добавления доп. кадра. Отдельно от проблем загрузки: они
        /// живут до следующего действия, а не до перезагрузки каталога.
        /// </summary>
        private readonly List<string> _extraProblems = new List<string>();

        /// <summary>Подпись списка доп. мест: пересборка только при изменении.</summary>
        private string _placeSignature;
        private readonly CatalogFilter _filter = new CatalogFilter();
        private readonly List<CatalogEntry> _allEntries = new List<CatalogEntry>();
        private readonly List<CatalogEntry> _visible = new List<CatalogEntry>();
        private readonly List<VisualElement> _pendingPreviews = new List<VisualElement>();
        private readonly Dictionary<string, Texture2D> _previews =
            new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        private Texture2D _detailTexture;
        private string _detailTexturePath;

        /// <summary>
        /// Последний известный размер места под превью. Нужен только как
        /// запасной, пока раскладка не посчитана: рабочий размер берётся
        /// у элемента в момент пересчёта.
        /// </summary>
        private Rect _previewBox;

        private float _appliedPanelWidth;
        private float _appliedPreviewWidth;
        private float _appliedPreviewHeight;

        /// <summary>
        /// Выбран пул. <c>null</c> — пул не выбран; это НЕ значит «показать
        /// всё»: у дерева три состояния, и «всё» — отдельное (оба поля null).
        /// </summary>
        private PoolInfo _selectedPool;

        /// <summary>Выбрана группа целиком (кнопка «Все пулы группы»).</summary>
        private CatalogGroup _selectedGroup;

        /// <summary>Выбран кадр проекта. С кандидатом взаимоисключающе.</summary>
        private CatalogEntry _selected;

        /// <summary>Выбран кандидат. С кадром проекта взаимоисключающе.</summary>
        private ReviewCandidate _selectedCandidate;

        /// <summary>
        /// Центр открыт на очереди ревью: показываются кандидаты плитками, а не
        /// кадры проекта. Это РЕЖИМ центра, а не свойство кандидата: очередь
        /// закрывается, когда человек выбрал в дереве пул или группу.
        /// </summary>
        private bool _reviewMode;

        /// <summary>Кнопка «На ревью» вверху левой панели.</summary>
        private Button _reviewSection;

        /// <summary>Две взаимоисключающие страницы центра.</summary>
        private VisualElement _catalogPanel;
        private VisualElement _reviewPanel;

        private Label _reviewHeader;
        private ScrollView _reviewGrid;

        /// <summary>Полоса прогресса ВСЕЙ заявки.</summary>
        private ProgressBar _generationBar;

        /// <summary>Полоса прогресса ТЕКУЩЕГО КАДРА. Вторая, а не вторая по счёту:
        /// на четырёх кадрах общий процент идёт рывками по 25, и без отдельной
        /// полосы кадра видно только эти рывки. Сбрасывается в ноль на каждом
        /// новом кадре — прогон обязан пройти её от 0 до 100 столько раз,
        /// сколько кадров заказано.</summary>
        private ProgressBar _frameBar;

        /// <summary>Полоса прогресса ТЕКУЩЕГО ЭТАПА: вырезка, проверка,
        /// приёмка. Этап — самый мелкий шаг, и именно он раньше был молчанием
        /// на десятки секунд.</summary>
        private ProgressBar _stageBar;

        private Label _generationTitle;
        private Label _generationStatus;
        private Label _generationLog;
        private Label _generationPhase;
        private Label _generationEta;
        private Label _overallCaption;
        private Label _frameCaption;
        private Label _stageCaption;

        /// <summary>
        /// Список кадров заявки со статусами. Полосы показывают числа, а не
        /// ответ на вопрос «что сейчас рисуется, а что ещё ждёт», — а это
        /// ровно то, чего не хватало, чтобы прогон не выглядел зависшим.
        /// </summary>
        private VisualElement _jobList;

        /// <summary>
        /// Метка последнего события ЭТАПА. Отдельно от <c>_progressSignature</c>:
        /// смена этапа обнуляет отсчёт молчания, иначе новый этап стартовал бы
        /// с подписи «подвисает».
        /// </summary>
        private string _stageSignature;

        private double _stageChangedAt;

        // ---- страница «Генератор» в правой панели ----
        // Кнопки драйвера и сервера, состояние стенда, прогресс и фильтры
        // Правая панель — три вкладки, а не две страницы. Раньше генератор
        // открывался кнопкой в шапке ОКНА, а внутри колонки жили только
        // «объект» и «генератор»; промпты, ради которых человек и открывает
        // студию, показывать было негде. Вкладки стоят в колонке, которой и
        // принадлежат: они переключают её содержимое, а не всё окно.

        private Button _tabObject;
        private Button _tabPrompts;
        private Button _tabGenerator;
        private VisualElement _detailPanel;
        private VisualElement _promptsPanel;
        private VisualElement _generatorPanel;

        private VisualElement _promptsTree;
        private TextField _promptsSearch;
        private Button _promptsRefresh;
        private Label _promptsStatus;

        /// <summary>Промпты стенда, разобранные из ответа драйвера.</summary>
        private ArtPrompts _prompts;

        /// <summary>
        /// Подпись последнего разобранного JSON. Промпты весят ~60 КБ, и
        /// разбирать их на каждом опросе (раз в 150 мс) означало бы жечь
        /// время впустую: стенд отвечает только по запросу.
        /// </summary>
        private string _promptsSignature;

        /// <summary>Правая панель открыта на странице генератора, а не объекта.</summary>
        private bool _generatorOpen => _rightPage == RightPage.Generator;

        /// <summary>Какая вкладка правой панели открыта.</summary>
        private RightPage _rightPage = RightPage.Object;

        /// <summary>
        /// Три вкладки правой панели. Перечисление, а не три bool: состояние
        /// «открыты две сразу» при трёх флагах не выражается, а выглядит
        /// именно так при первой же правке.
        /// </summary>
        private enum RightPage
        {
            Object,
            Prompts,
            Generator,
        }

        private VisualElement _driverDot;
        private VisualElement _serverDot;
        private Label _infoDriver;
        private Label _infoServer;
        private VisualElement _infoModel;
        private Label _infoStyle;
        private Label _infoPools;
        private Label _infoFree;
        private Label _infoPoolFree;
        private Label _infoQueue;
        private Label _infoActivity;

        /// <summary>
        /// Когда прогресс текущего кадра сменился последний раз. По этому
        /// времени окно понимает, идёт ли работа «внутри шага» (загрузка
        /// моделей, шаг сэмплера) и надо ли показать бегущую подсветку полосы.
        /// </summary>
        private double _progressChangedAt;

        /// <summary>Подпись последнего известного прогресса — по ней ловим смену.</summary>
        private string _progressSignature;

        /// <summary>
        /// Видимое значение полосы «Текущий этап». Настоящий процент живёт в
        /// состоянии, а полоса едет к нему плавно — см. <see cref="GenerationProgress.Eased"/>.
        /// </summary>
        private float _shownOverall;

        private float _shownFrame;
        private float _shownStage;

        /// <summary>
        /// Кадр, для которого считается плавное значение. При смене кадра
        /// показанные значения обнуляются мгновенно: анимация нужна внутри
        /// кадра, а перенос процентов предыдущего кадра на новый читался бы
        /// как «почти готов» до того, как стенд к нему приступил.
        /// </summary>
        private string _shownFrameId;

        private string _shownStageKey;

        /// <summary>Настоящие значения, к которым едут видимые полосы.</summary>
        private float _overallTarget;

        private float _frameTarget;
        private float _stageTarget;

        /// <summary>Метка прошлого тика — из неё берётся шаг плавности.</summary>
        private double _lastTickAt = -1;

        /// <summary>Было ли в прошлом опросе живое задание — ловим переход «идёт → готово».</summary>
        private bool _wasGenerating;

        /// <summary>Идёт ли генерация прямо сейчас; нужен, чтобы гасить «Перегенерировать».</summary>
        private bool _generationRunning;

        /// <summary>
        /// До какого времени держать на экране «Готово» после завершения. Без
        /// задержки готовый результат мелькнул бы на один опрос (150 мс) и
        /// исчез — человек не успел бы увидеть, что прогон кончился успешно.
        /// </summary>
        private double _generationLingerUntil;

        /// <summary>
        /// Что делает кнопка «Стенд»: ничего, поднимает, работает, гасит.
        /// Переходы считает <see cref="StandControl"/> — здесь только память
        /// о текущем состоянии и сроке.
        /// </summary>
        private StandPhase _standPhase = StandPhase.Down;

        /// <summary>Срок текущего перехода (время редактора). 0 — бессрочно.</summary>
        private double _standDeadline;

        [MenuItem("Woodberry/Art Studio")]
        internal static void Open()
        {
            ArtStudioWindow window = GetWindow<ArtStudioWindow>("Арт-студия");
            window.minSize = new Vector2(1040f, 680f);
            window.Show();
        }

        private void CreateGUI()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (tree == null)
            {
                Debug.LogError($"Woodberry: не найдена разметка окна арт-студии: {UxmlPath}");
                return;
            }

            tree.CloneTree(rootVisualElement);

            _projectRoot = ArtStudioPaths.ProjectRootFromDataPath(Application.dataPath);

            CacheElements();
            WireEvents();

            LoadState();
            SetupDriver();

            // Превью приходят из AssetDatabase не сразу: раз в 150 мс
            // дозапрашиваем то, чего ещё нет. Через планировщик окна, а не
            // через EditorApplication.update, — вместе с окном останавливается.
            rootVisualElement.schedule.Execute(PollPreviews).Every(150);

            // Драйвер отвечает событиями в файл: дочитываем их тем же
            // планировщиком, что и превью, — вместе с окном замолкает.
            rootVisualElement.schedule.Execute(PollDriver).Every(150);
        }

        private void CacheElements()
        {
            _status = rootVisualElement.Q<Label>("status");
            _problems = rootVisualElement.Q<Label>("problems");
            _framesHeader = rootVisualElement.Q<Label>("framesHeader");
            _detailHeader = rootVisualElement.Q<Label>("detailHeader");
            _frameTitle = rootVisualElement.Q<Label>("frameTitle");
            _frameId = rootVisualElement.Q<Label>("frameId");
            _framePath = rootVisualElement.Q<Label>("framePath");
            _frameStatus = rootVisualElement.Q<Label>("frameStatus");
            _frameCoverage = rootVisualElement.Q<Label>("frameCoverage");
            _preview = rootVisualElement.Q<Image>("preview");

            // Кадр детали держит пропорции сам (точный размер считает
            // PreviewFit), а режим стоит явно: иначе его задавало бы значение
            // по умолчанию, и правка раскладки могла бы это молча изменить.
            _preview.scaleMode = ScaleMode.ScaleToFit;
            _previewHolder = rootVisualElement.Q<VisualElement>("previewHolder");
            _rightPanel = rootVisualElement.Q<VisualElement>("panelRight");

            // Соседние колонки нужны, чтобы знать, сколько ширины окно может
            // отдать правому блоку, не сломав левую и центральную.
            _leftPanel = rootVisualElement.Q<VisualElement>(null, "panel-left");
            _centerPanel = rootVisualElement.Q<VisualElement>(null, "panel-center");
            _approve = rootVisualElement.Q<Button>("approve");
            _reject = rootVisualElement.Q<Button>("reject");
            _openFolder = rootVisualElement.Q<Button>("openFolder");
            _accept = rootVisualElement.Q<Button>("accept");
            _rejectCandidate = rootVisualElement.Q<Button>("rejectCandidate");
            _candidateActions = rootVisualElement.Q<VisualElement>("candidateActions");
            _frameActions = rootVisualElement.Q<VisualElement>("frameActions");
            _rejectComment = rootVisualElement.Q<VisualElement>("rejectComment");
            _comment = rootVisualElement.Q<TextField>("comment");
            _search = rootVisualElement.Q<ToolbarSearchField>("search");
            _statusFilter = rootVisualElement.Q<ToolbarMenu>("statusFilter");
            _kindFilter = rootVisualElement.Q<ToolbarMenu>("kindFilter");
            _onlyRegenerable = rootVisualElement.Q<ToolbarToggle>("onlyRegenerable");
            _refreshButton = rootVisualElement.Q<Button>("refresh");
            _categories = rootVisualElement.Q<ScrollView>("categories");
            _frames = rootVisualElement.Q<ListView>("frames");
            _standButton = rootVisualElement.Q<ToolbarButton>("standButton");
            _regenButton = rootVisualElement.Q<Button>("regenMarked");
            _addRow = rootVisualElement.Q<VisualElement>("addRow");
            _addHint = rootVisualElement.Q<Label>("addHint");
            _addCount = rootVisualElement.Q<IntegerField>("addCount");
            _addButton = rootVisualElement.Q<Button>("addButton");
            _reviewSection = rootVisualElement.Q<Button>("reviewSection");
            _catalogPanel = rootVisualElement.Q<VisualElement>("catalogPanel");
            _reviewPanel = rootVisualElement.Q<VisualElement>("reviewPanel");
            _reviewHeader = rootVisualElement.Q<Label>("reviewHeader");
            _reviewGrid = rootVisualElement.Q<ScrollView>("reviewGrid");
            _generationBar = rootVisualElement.Q<ProgressBar>("generationBar");
            _frameBar = rootVisualElement.Q<ProgressBar>("frameBar");
            _stageBar = rootVisualElement.Q<ProgressBar>("stageBar");
            _generationTitle = rootVisualElement.Q<Label>("generationTitle");
            _generationStatus = rootVisualElement.Q<Label>("generationStatus");
            _generationLog = rootVisualElement.Q<Label>("generationLog");
            _generationPhase = rootVisualElement.Q<Label>("generationPhase");
            _generationEta = rootVisualElement.Q<Label>("generationEta");
            _overallCaption = rootVisualElement.Q<Label>("overallCaption");
            _frameCaption = rootVisualElement.Q<Label>("frameCaption");
            _stageCaption = rootVisualElement.Q<Label>("stageCaption");
            _jobList = rootVisualElement.Q<VisualElement>("jobList");

            _tabObject = rootVisualElement.Q<Button>("tabObject");
            _tabPrompts = rootVisualElement.Q<Button>("tabPrompts");
            _tabGenerator = rootVisualElement.Q<Button>("tabGenerator");
            _detailPanel = rootVisualElement.Q<VisualElement>("detailPanel");
            _promptsPanel = rootVisualElement.Q<VisualElement>("promptsPanel");
            _generatorPanel = rootVisualElement.Q<VisualElement>("generatorPanel");
            _promptsTree = rootVisualElement.Q<VisualElement>("promptsTree");
            _promptsSearch = rootVisualElement.Q<TextField>("promptsSearch");
            _promptsRefresh = rootVisualElement.Q<Button>("promptsRefresh");
            _promptsStatus = rootVisualElement.Q<Label>("promptsStatus");
            if (_promptsSearch != null)
            {
                _promptsSearch.tooltip = "Поиск по всем разделам промптов";
            }

            _driverDot = rootVisualElement.Q<VisualElement>("driverDot");
            _serverDot = rootVisualElement.Q<VisualElement>("serverDot");
            _infoDriver = rootVisualElement.Q<Label>("infoDriver");
            _infoServer = rootVisualElement.Q<Label>("infoServer");
            _infoModel = rootVisualElement.Q<VisualElement>("infoModel");
            _infoStyle = rootVisualElement.Q<Label>("infoStyle");
            _infoPools = rootVisualElement.Q<Label>("infoPools");
            _infoFree = rootVisualElement.Q<Label>("infoFree");
            _infoPoolFree = rootVisualElement.Q<Label>("infoPoolFree");
            _infoQueue = rootVisualElement.Q<Label>("infoQueue");
            _infoActivity = rootVisualElement.Q<Label>("infoActivity");
            _placesPanel = rootVisualElement.Q<VisualElement>("placesPanel");
            _placesToggle = rootVisualElement.Q<Button>("placesToggle");
            _placeList = rootVisualElement.Q<ScrollView>("placeList");
            _placeGroup = rootVisualElement.Q<DropdownField>("placeGroup");
            _placeId = rootVisualElement.Q<TextField>("placeId");
            _placeLabel = rootVisualElement.Q<TextField>("placeLabel");
            _placeScene = rootVisualElement.Q<TextField>("placeScene");
            _placeAdd = rootVisualElement.Q<Button>("placeAdd");
        }

        private void WireEvents()
        {
            _refreshButton.clicked += LoadState;
            _standButton.clicked += ToggleStand;
            _tabObject.clicked += () => SetRightPage(RightPage.Object);
            _tabPrompts.clicked += () => SetRightPage(RightPage.Prompts);
            _tabGenerator.clicked += () => SetRightPage(RightPage.Generator);
            _promptsRefresh.clicked += RequestPromptsNow;
            if (_promptsSearch != null)
            {
                _promptsSearch.RegisterValueChangedCallback(_ =>
                    RenderPrompts(_promptsSearch.value));
            }
            _addButton.clicked += AddVariants;
            _regenButton.clicked += RegenerateMarked;
            _regenButton.SetEnabled(false);

            // Область доп. мест раскрывается кнопкой в нижней строке: она
            // нужна редко, и держать её открытой значило бы отнимать высоту
            // у кадра на каждом запуске окна.
            _placesPanel.style.display = DisplayStyle.None;
            _placesToggle.clicked += TogglePlaces;

            _search.RegisterValueChangedCallback(change => OnSearch(change.newValue));

            _statusFilter.menu.AppendAction("Статус: все", _ => OnStatusFilter(null));
            _statusFilter.menu.AppendAction("Статус: не смотрели", _ => OnStatusFilter(ArtStatus.New));
            _statusFilter.menu.AppendAction("Статус: приняты", _ => OnStatusFilter(ArtStatus.Approved));
            _statusFilter.menu.AppendAction("Статус: брак", _ => OnStatusFilter(ArtStatus.Rejected));

            _kindFilter.menu.AppendAction("Все кадры", _ => OnKindFilter(null));
            _kindFilter.menu.AppendAction("Только портреты", _ => OnKindFilter(ArtAssetKind.Portrait));
            _kindFilter.menu.AppendAction("Только фоны", _ => OnKindFilter(ArtAssetKind.Background));

            _onlyRegenerable.RegisterValueChangedCallback(change =>
            {
                _filter.OnlyRegenerable = change.newValue;
                ReapplyFilter();
            });

            _approve.clicked += ApproveSelected;
            _reject.clicked += RejectSelected;
            _openFolder.clicked += OpenSelectedFolder;

            // Действия кандидата. «Принять» переносит файл драйвером — это
            // не то же самое, что «отметить принятым» у кадра проекта.
            _accept.clicked += AcceptSelectedCandidate;
            _rejectCandidate.clicked += RejectSelectedCandidate;

            // Группы мест — из таблицы переноса, а не из каталога: каталог
            // знает только занятые места, и до его ответа список был бы пуст.
            _placeGroup.choices = StandGroupMapper.BackgroundStandGroups.ToList();
            if (_placeGroup.choices.Count > 0)
            {
                _placeGroup.SetValueWithoutNotify(_placeGroup.choices[0]);
            }

            _placeAdd.clicked += AddExtraPlace;

            // Превью обязано держать пропорции кадра, поэтому его размер
            // пересчитывается на каждое изменение отведённого места.
            _previewHolder.RegisterCallback<GeometryChangedEvent>(OnPreviewHolderResized);

            // И на изменение самого окна: высота окна задаёт ширину кадра,
            // а место под кадром меняется не на каждом ресайзе.
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(OnWindowResized);

            // По каждому нажатию клавиши не пишем: журнал ревью переживёт
            // потерю последнего символа, но не тысячу записей на абзац.
            _comment.RegisterCallback<FocusOutEvent>(_ => SaveComment());

            _frames.makeItem = MakeFrameRow;
            _frames.bindItem = BindFrameRow;
            _frames.itemsSource = _visible;
            _frames.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            // Высота строки = миниатюра 96×144 плюс отступы строки: кадр теперь
            // 2:3 и крупнее, и старая высота 54 обрезала бы его.
            _frames.fixedItemHeight = 152f;
            _frames.selectionChanged += OnFrameSelected;

            // Клик по «На ревью» открывает очередь в центре, а не раскрывает
            // мелкий список слева: смотреть кандидатов надо крупно, а решать —
            // кнопкой под каждым.
            _reviewSection.clicked += () => SetReviewMode(!_reviewMode);

            // Плитки кандидатов ложатся в строку и переносятся — без этого
            // ScrollView строит их в одну колонку. Правит контейнер содержимого,
            // а не сам ScrollView: его собственный флекс к содержимому не относится,
            // и USS-селектор до контейнера не дотягивается.
            _reviewGrid.contentContainer.style.flexDirection = FlexDirection.Row;
            _reviewGrid.contentContainer.style.flexWrap = Wrap.Wrap;
            _reviewGrid.contentContainer.style.alignContent = Align.FlexStart;

            _generationBar.lowValue = 0f;
            _generationBar.highValue = 100f;
            _generationBar.value = 0f;
            _frameBar.lowValue = 0f;
            _frameBar.highValue = 100f;
            _frameBar.value = 0f;
            _stageBar.lowValue = 0f;
            _stageBar.highValue = 100f;
            _stageBar.value = 0f;
            _reviewPanel.style.display = DisplayStyle.None;

            // По умолчанию правая панель показывает объект: с него начинается
            // работа, а генератор и промпты открывают вкладками.
            SetRightPage(RightPage.Object);

            // Полосы движются планировщиком окна: UI Toolkit сам не анимирует,
            // а держать для этого отдельный поток незачем.
            rootVisualElement.schedule.Execute(TickBars).Every(80);
        }

        /// <summary>
        /// Переключает вкладку правой панели.
        ///
        /// Вкладки взаимоисключающие и занимают одну колонку: держать их
        /// рядом значило бы делить высоту, которой и так не хватает на кадр.
        /// Заголовок вкладки подсвечивается, а не меняет текст на «назад» —
        /// переключатель с именами вкладок читается однозначнее кнопки,
        /// которая называет действие.
        /// </summary>
        private void SetRightPage(RightPage page)
        {
            _rightPage = page;

            _detailPanel.style.display =
                page == RightPage.Object ? DisplayStyle.Flex : DisplayStyle.None;
            _promptsPanel.style.display =
                page == RightPage.Prompts ? DisplayStyle.Flex : DisplayStyle.None;
            _generatorPanel.style.display =
                page == RightPage.Generator ? DisplayStyle.Flex : DisplayStyle.None;

            _tabObject.EnableInClassList("is-active", page == RightPage.Object);
            _tabPrompts.EnableInClassList("is-active", page == RightPage.Prompts);
            _tabGenerator.EnableInClassList("is-active", page == RightPage.Generator);

            // Вернулись к объекту — обновляем его описание: пока показывали
            // генератор, выбор мог измениться (или он пуст).
            if (page == RightPage.Object)
            {
                UpdateDetailPanel();
            }

            if (page == RightPage.Prompts)
            {
                // Промпты спрашиваются у стенда, а не лежат в проекте: иначе
                // вкладка показывала бы то, чем рисует ВЧЕРА. Запрашиваем при
                // каждом входе — стенд ответит мгновенно, а данные всегда свежие.
                RequestPromptsNow();
            }

            LayoutPreview();
        }

        /// <summary>Просит у драйвера промпты стенда.</summary>
        private void RequestPromptsNow()
        {
            _promptsSignature = null;
            if (_client != null)
            {
                _client.RequestPrompts();
            }

            SetPromptStatus("Запрашиваю промпты у стенда…");
        }

        private void SetPromptStatus(string text)
        {
            if (_promptsStatus != null && _promptsStatus.text != text)
            {
                _promptsStatus.text = text;
            }
        }

        /// <summary>
        /// Разбирает ответ стенда и рисует страницу промптов.
        ///
        /// Вызывается на каждом опросе, но разбирает только когда ответ
        /// изменился: стенд не шлёт промпты сам, а JSON весит ~60 КБ, и
        /// разбор на каждом тике (раз в 150 мс) жег бы время впустую.
        /// </summary>
        private void UpdatePrompts(ArtDriverState state)
        {
            string json = state.PromptsJson;
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            if (json == _promptsSignature)
            {
                return;
            }

            _promptsSignature = json;

            if (!ArtPrompts.TryParse(json, out ArtPrompts prompts))
            {
                _prompts = null;
                SetPromptStatus("Стенд ответил промптами, которые не разобрались");
                RenderPrompts(null);
                return;
            }

            _prompts = prompts;
            SetPromptStatus(string.Empty);
            RenderPrompts(_promptsSearch != null ? _promptsSearch.value : string.Empty);
        }

        /// <summary>
        /// Рисует промпты по разделам. Раскладка повторяет `scene.py`: стиль,
        /// свет, негатив, персонажи, места, параметры — те же разрезы, по
        /// которым промпт собирается на стенде.
        ///
        /// Списки свёрнуты в группы: 112 персонажей это не «посмотреть», а
        /// «найти одного». Поиск раскрывает группы с совпадением и прячет
        /// остальные — иначе на 112 записях он бесполезен.
        /// </summary>
        private void RenderPrompts(string query)
        {
            if (_promptsTree == null)
            {
                return;
            }

            _promptsTree.Clear();

            if (_prompts == null)
            {
                var empty = new Label(
                    "Промпты не загружены. Открой вкладку «Промпты», когда драйвер поднят.");
                empty.AddToClassList("prompt-item-text");
                _promptsTree.Add(empty);
                return;
            }

            string filter = (query ?? string.Empty).Trim();
            bool filtering = filter.Length > 0;

            // --- Стиль
            var styleGroup = BeginGroup("Стиль рисунка", filtering);
            foreach (PromptStyle style in _prompts.Styles)
            {
                if (filtering && !Matches(filter, style.key, style.text))
                {
                    continue;
                }

                styleGroup.Add(PromptText($"{style.key}\n{style.text}", style.key));
            }
            EndGroup(styleGroup, filtering, "Стиль");

            // --- Свет
            var lightGroup = BeginGroup("Свет", filtering);
            foreach (PromptLight light in _prompts.Lights)
            {
                if (filtering && !Matches(filter, light.key, light.text))
                {
                    continue;
                }

                lightGroup.Add(PromptText($"{light.key}\n{light.text}", light.key));
            }
            EndGroup(lightGroup, filtering, "Свет");

            // --- Негатив
            var negGroup = BeginGroup("Негативный промпт", filtering);
            if (!filtering
                || Matches(filter, "общий", _prompts.NegativeCommon))
            {
                negGroup.Add(PromptText(_prompts.NegativeCommon, "негатив общий"));
            }
            if (!filtering
                || Matches(filter, "фон", _prompts.NegativeBackground))
            {
                negGroup.Add(PromptText(
                    _prompts.NegativeBackground, "негатив фонов"));
            }
            EndGroup(negGroup, filtering, "Негатив");

            // --- Персонажи
            var charGroup = BeginGroup(
                $"Персонажи ({_prompts.Characters.Length})", filtering);
            foreach (PromptCharacter ch in _prompts.Characters)
            {
                if (filtering && !Matches(filter, ch.id, ch.name, ch.light,
                        ch.pose, ch.subject))
                {
                    continue;
                }

                charGroup.Add(PromptItemTitle($"{ch.name} · {ch.id}", ch.light));
                charGroup.Add(PromptText(ch.pose, $"{ch.id}: поза"));
                charGroup.Add(PromptText(ch.subject, $"{ch.id}: описание"));
            }
            EndGroup(charGroup, filtering, "Персонажи");

            // --- Места
            var bgGroup = BeginGroup($"Места ({_prompts.Backgrounds.Length})", filtering);
            foreach (PromptBackground bg in _prompts.Backgrounds)
            {
                if (filtering && !Matches(filter, bg.id, bg.name, bg.scene))
                {
                    continue;
                }

                bgGroup.Add(PromptItemTitle($"{bg.name} · {bg.id}", bg.light));
                bgGroup.Add(PromptText(bg.scene, $"{bg.id}: сцена"));
            }
            EndGroup(bgGroup, filtering, "Места");

            // --- Параметры
            var genGroup = BeginGroup("Параметры генерации", filtering);
            if (_prompts.Gen != null && (!filtering
                || Matches(filter, "параметры генерации", DescribeGen(_prompts.Gen))))
            {
                foreach (string line in DescribeGen(_prompts.Gen).Split('\n'))
                {
                    genGroup.Add(PromptItemTitle(line.Trim(), null));
                }
            }
            EndGroup(genGroup, filtering, "Параметры");

            if (filtering && _promptsTree.childCount == 0)
            {
                var none = new Label($"Ничего не нашлось: «{filter}»");
                none.AddToClassList("prompt-item-text");
                _promptsTree.Add(none);
            }
        }

        private static string DescribeGen(PromptGen gen)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("шагов: ").Append(gen.steps).Append('\n');
            sb.Append("CFG: ").Append(gen.cfg).Append('\n');
            sb.Append("планировщик: ").Append(gen.scheduler).Append('\n');
            sb.Append("база сида: ").Append(gen.seed_base).Append('\n');
            sb.Append("кадр персонажа: ").Append(Size(gen.subject_size)).Append('\n');
            sb.Append("кадр фона: ").Append(Size(gen.background_size)).Append('\n');
            sb.Append("хрома: ").Append(Size(gen.chroma));
            return sb.ToString();
        }

        private static string Size(int[] value)
        {
            return value == null || value.Length == 0
                ? "—"
                : string.Join("×", value);
        }

        private static bool Matches(string filter, params string[] fields)
        {
            if (fields == null)
            {
                return false;
            }

            foreach (string field in fields)
            {
                if (!string.IsNullOrEmpty(field)
                    && field.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Создаёт группу. При поиске группа создаётся раскрытой и останется
        /// раскрытой: иначе фильтр отработал бы, а человек увидел бы пустоту.
        /// </summary>
        private Foldout BeginGroup(string title, bool filtering)
        {
            var foldout = new Foldout { text = title, value = filtering };
            _promptsTree.Add(foldout);
            return foldout;
        }

        private void EndGroup(Foldout group, bool filtering, string title)
        {
            if (filtering && group.childCount == 0)
            {
                // Пустая группа при поиске — шум: человек не знает, что
                // раздела нет или что в нём нет совпадений.
                group.RemoveFromHierarchy();
            }
        }

        private static Label PromptText(string body, string tooltip)
        {
            var label = new Label(string.IsNullOrEmpty(body) ? "—" : body);
            label.AddToClassList("prompt-item-text");
            if (!string.IsNullOrEmpty(tooltip))
            {
                label.tooltip = tooltip;
            }

            return label;
        }

        private static Label PromptItemTitle(string body, string meta)
        {
            var label = new Label(body);
            label.AddToClassList("prompt-item-title");
            if (!string.IsNullOrEmpty(meta))
            {
                label.tooltip = meta;
            }

            return label;
        }

        /// <summary>
        /// Сводка стенда по строке на пункт: «Модель: …», «LoRA: …»,
        /// «Энкодер: …». Раньше это был один Label со склейкой через «·», то
        /// есть пять длинных описаний в одну строку, где нужное не найти.
        ///
        /// Пустая строка до первого прогона объясняется словами, а не тире:
        /// тире выглядит как сломанная панель, а на самом деле сводка просто
        /// приходит из шапки `shoot.py` при первом прогоне.
        /// </summary>
        private void RenderStandInfo(string[] lines)
        {
            if (_infoModel == null)
            {
                return;
            }

            _infoModel.Clear();

            if (lines == null || lines.Length == 0)
            {
                var empty = new Label("сводка придёт с первым прогоном");
                empty.AddToClassList("gen-model-empty");
                _infoModel.Add(empty);
                return;
            }

            foreach (string line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var label = new Label(line.Trim());
                label.AddToClassList("gen-model-line");
                _infoModel.Add(label);
            }
        }

        /// <summary>
        /// Плавное движение полос к настоящим процентам. Идёт раз в 80 мс:
        /// UI Toolkit сам не анимирует, а держать для этого отдельный поток
        /// незачем.
        ///
        /// Раньше здесь ездила бегущая подсветка «работа идёт». Её убрали:
        /// второе движение поверх первого — это шум, и «работа идёт» уже видно
        /// по тому, что полоса ползёт.
        /// </summary>
        private void TickBars()
        {
            if (_generationBar == null || _frameBar == null || _stageBar == null)
            {
                return;
            }

            _shownOverall = GenerationProgress.Eased(_shownOverall, _overallTarget,
                _lastTickAt < 0 ? 0 : EditorApplication.timeSinceStartup - _lastTickAt);
            _shownFrame = GenerationProgress.Eased(_shownFrame, _frameTarget,
                _lastTickAt < 0 ? 0 : EditorApplication.timeSinceStartup - _lastTickAt);
            _shownStage = GenerationProgress.Eased(_shownStage, _stageTarget,
                _lastTickAt < 0 ? 0 : EditorApplication.timeSinceStartup - _lastTickAt);
            _lastTickAt = EditorApplication.timeSinceStartup;

            _generationBar.value = _shownOverall;
            _frameBar.value = _shownFrame;
            _stageBar.value = _shownStage;
        }

        // ------------------------------------------------------------- состояние

        private void LoadState()
        {
            var problems = new List<string>();

            ArtSettings.LoadResult settings = ArtSettings.Load(ArtStudioPaths.Config(_projectRoot));
            _settings = settings.Settings;
            problems.AddRange(settings.Problems);

            _feedback = new FeedbackStore();
            problems.AddRange(_feedback.Load(ArtStudioPaths.Feedback(_projectRoot)).Problems);

            _extraPools = new ExtraPoolsFile();
            problems.AddRange(_extraPools.Load(ArtStudioPaths.ExtraPools(_projectRoot)).Problems);
            _extraProblems.Clear();
            _placeSignature = null;

            CatalogScanner.ScanResult scan = CatalogScanner.Scan(
                Path.Combine(_projectRoot, CatalogScanner.DialogueRoot));
            problems.AddRange(scan.Problems);

            IReadOnlyList<string> unmapped = StandCatalogFallback.Apply(scan.Entries);
            foreach (string path in unmapped)
            {
                problems.Add($"группа не узнана, кадр без id стенда: {path}");
            }

            _allEntries.Clear();
            _allEntries.AddRange(scan.Entries);
            _previews.Clear();
            _detailTexture = null;
            _detailTexturePath = null;

            // Проблемы загрузки переживают перерисовки: их подмешивает в
            // строку проблем и опрос драйвера, а не только эта загрузка.
            _loadProblems.Clear();
            _loadProblems.AddRange(problems);

            ReapplyFilter(_loadProblems);

            // Кандидаты читаются с диска, а не из событий: папка review/ —
            // источник правды. Скан идёт и по «Обновить», поэтому кандидат,
            // положенный драйвером при закрытом окне, виден сразу.
            RescanReview();
            RenderLeftPanel();

            // «Обновить» перечитывает и каталог стенда, а не только проект:
            // иначе правка доп. пулов (файл читает стенд) вступала бы в силу
            // лишь после передёргивания драйвера, и человек искал бы причину
            // в файле, который на самом деле уже верный.
            _client?.RequestCatalog();
        }

        private void ReapplyFilter(IReadOnlyList<string> extraProblems = null)
        {
            string keep = _selected?.AssetPath;

            // Пул переживает пересборку модели. Объекты PoolInfo каждый раз
            // новые, и сравнение по ссылке теряло выбор после каждой правки —
            // список кадров прыгал на «все пулы» после пометки брака.
            string keepPool = _selectedPool == null
                ? null
                : $"{_selectedPool.Kind}|{_selectedPool.Group}|{_selectedPool.Name}";
            string keepGroup = _selectedGroup?.Key;

            _model = CatalogModel.Build(_allEntries, _feedback, _filter);

            _selectedPool = keepPool == null
                ? null
                : _model.Groups.SelectMany(group => group.Pools)
                    .FirstOrDefault(pool => $"{pool.Kind}|{pool.Group}|{pool.Name}" == keepPool);

            _selectedGroup = keepGroup == null
                ? null
                : _model.Groups.FirstOrDefault(group => group.Key == keepGroup);

            RenderLeftPanel();
            BuildFrameList(keep);
            WriteStatus(extraProblems);
        }

        private void OnSearch(string value)
        {
            _filter.Search = value;
            ReapplyFilter();
        }

        private void OnStatusFilter(ArtStatus? status)
        {
            _filter.Status = status;
            _statusFilter.text = status == null
                ? "Статус: все"
                : $"Статус: {StatusText(status.Value)}";
            ReapplyFilter();
        }

        private void OnKindFilter(ArtAssetKind? kind)
        {
            _filter.Kind = kind;
            _kindFilter.text = kind == null
                ? "Все кадры"
                : kind == ArtAssetKind.Portrait ? "Только портреты" : "Только фоны";
            ReapplyFilter();
        }

        // ------------------------------------------------------------------ отрисовка

        /// <summary>
        /// Левая панель: выбор того, что смотреть, и очередь ревью.
        ///
        /// Три раздела верхнего уровня: «Портреты», «Фоны» и «На ревью».
        /// Внутри первых двух — типы (City, Craftsmen…), внутри типов — пулы.
        /// Очередь ревью здесь, а не в центре: кандидат — это то, чего в
        /// проекте ещё НЕТ, и его место рядом с выбором, а не среди кадров.
        ///
        /// Пересобирается целиком по подписи: узлов сотня, и раскрытость
        /// разделов окно помнит само (<see cref="_collapsed"/>), иначе каждая
        /// пересборка раскрывала бы всё заново.
        /// </summary>
        private void RenderLeftPanel()
        {
            // Кнопка «На ревью» и плитки кандидатов обновляются ДО раннего
            // выхода по подписи: подпись — про очередь дерева, а очередь ревью
            // меняется по сканy папки и живёт вне дерева.
            UpdateReviewSection();
            if (_reviewMode)
            {
                RenderReviewGrid();
            }

            string signature = BuildLeftSignature();
            if (signature == _leftSignature)
            {
                return;
            }

            _leftSignature = signature;

            // Очередь неготовых превью — это список КАДРОВ; дереву она не
            // принадлежит, и чистить её здесь значило бы гасить миниатюры,
            // которые ещё не успели загрузиться.
            _categories.Clear();

            // Возврат к полному каталогу — отдельная кнопка, а не «Все пулы
            // группы»: та кнопка обязана показывать свою группу, и раньше она
            // молча показывала вообще всё.
            var everything = new Button(() => SelectGroup(null)) { text = "Все кадры проекта" };
            everything.AddToClassList("category-button");
            if (!_reviewMode && _selectedPool == null && _selectedGroup == null)
            {
                everything.AddToClassList("category-button-selected");
            }

            _categories.Add(everything);

            foreach (ArtAssetKind kind in new[] { ArtAssetKind.Portrait, ArtAssetKind.Background })
            {
                List<CatalogGroup> groups = _model.Groups
                    .Where(group => group.Kind == kind)
                    .ToList();
                if (groups.Count == 0)
                {
                    continue;
                }

                Foldout kindFoldout = MakeFoldout(
                    $"kind:{kind}",
                    $"{KindTitle(kind)} ({groups.Sum(group => group.FrameCount)})",
                    "kind-foldout");

                foreach (CatalogGroup group in groups)
                {
                    kindFoldout.Add(MakeGroupFoldout(group));
                }

                _categories.Add(kindFoldout);
            }

            if (_model.Groups.Count == 0)
            {
                _categories.Add(new Label(
                    "Ни один кадр не подходит под фильтр.\n" +
                    "Сними поиск или выбери другой статус.")
                {
                    name = "categories-empty"
                });
            }
        }

        private Foldout MakeGroupFoldout(CatalogGroup group)
        {
            Foldout foldout = MakeFoldout(
                $"group:{group.Key}",
                $"{GroupShortTitle(group)} ({group.FrameCount})",
                "category-foldout");

            CatalogGroup capturedGroup = group;
            var all = new Button(() => SelectGroup(capturedGroup)) { text = "Все пулы группы" };
            all.AddToClassList("category-button");
            if (ReferenceEquals(_selectedGroup, group))
            {
                all.AddToClassList("category-button-selected");
            }

            foldout.Add(all);

            foreach (PoolInfo pool in group.Pools)
            {
                foldout.Add(MakePoolButton(pool));
            }

            return foldout;
        }

        private Button MakePoolButton(PoolInfo pool)
        {
            int rejected = pool.Frames.Count(frame =>
                _feedback.StatusOf(frame.AssetPath) == ArtStatus.Rejected);
            int onReview = _reviewCandidates.Count(candidate =>
                CandidateBelongsToPool(candidate, pool));

            var parts = new List<string> { pool.Frames.Count.ToString() };
            if (onReview > 0)
            {
                parts.Add($"на ревью {onReview}");
            }

            if (rejected > 0)
            {
                parts.Add($"брак {rejected}");
            }

            var button = new Button(() => SelectPool(pool))
            {
                text = $"{pool.Name} ({string.Join(", ", parts)})"
            };
            button.AddToClassList("category-button");
            if (ReferenceEquals(_selectedPool, pool))
            {
                button.AddToClassList("category-button-selected");
            }

            if (!pool.CanRegenerate)
            {
                button.AddToClassList("category-button-nocatalog");
            }

            return button;
        }

        /// <summary>
        /// Раздел дерева с памятью о том, свёрнут он или раскрыт. Подписка
        /// ставится после конструктора: значение из него обработчик не
        /// дёргает, иначе память записала бы не то, что на экране.
        /// </summary>
        private Foldout MakeFoldout(string key, string text, string className)
        {
            var foldout = new Foldout { text = text, value = !_collapsed.Contains(key) };
            foldout.AddToClassList(className);
            foldout.RegisterValueChangedCallback(change =>
            {
                if (change.newValue)
                {
                    _collapsed.Remove(key);
                }
                else
                {
                    _collapsed.Add(key);
                }
            });

            return foldout;
        }

        /// <summary>
        /// Кнопка очереди вверху левой панели. Она называет счётчик и
        /// подсвечивается, когда очередь открыта; отдельным цветом — когда в
        /// ней есть кандидаты, но центр показывает каталог: иначе решение,
        /// которое ждёт, легко не заметить.
        /// </summary>
        private void UpdateReviewSection()
        {
            if (_reviewSection == null)
            {
                return;
            }

            _reviewSection.text = _reviewCandidates.Count > 0
                ? $"На ревью ({_reviewCandidates.Count})"
                : "На ревью";
            _reviewSection.EnableInClassList("category-button-selected", _reviewMode);
            _reviewSection.EnableInClassList(
                "review-has-items", !_reviewMode && _reviewCandidates.Count > 0);
        }

        /// <summary>
        /// Переключить центр между каталогом кадров и очередью ревью.
        ///
        /// Почему режим центра, а не панель очереди слева. Кандидату надо
        /// решить судьбу, а для этого его надо разглядеть: мелкая строка 28 px
        /// в узкой колонке для этого не годилась. В центре кандидат — крупная
        /// картинка 2:3 с кнопкой «Принять» под ней, как обычный кадр.
        ///
        /// Открытие снимает выбор пула и группы: центр показывает одно из
        /// двух, и «выбрана группа» одновременно с «открыта очередь» — это
        /// два состояния, показанные одним экраном.
        /// </summary>
        private void SetReviewMode(bool on)
        {
            _reviewMode = on;

            if (on)
            {
                _selectedPool = null;
                _selectedGroup = null;
                _frames.ClearSelection();
                _reviewSignature = null;
            }

            _catalogPanel.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            _reviewPanel.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

            RenderLeftPanel();

            if (!on)
            {
                BuildFrameList();
            }
        }

        /// <summary>
        /// Плитки кандидатов в центре. Пересобирается по подписи: в неё входит
        /// файл (время, размер), отметка и выбор, — иначе плитка не
        /// перерисовалась бы после пометки брака.
        ///
        /// Текстуры грузятся сразу, а не через отложенный опрос: кандидатов
        /// десятки, а не сотни, и <see cref="CandidateTexture"/> кэширует их
        /// после первого чтения.
        /// </summary>
        private void RenderReviewGrid()
        {
            if (_reviewGrid == null)
            {
                return;
            }

            string signature = string.Join("|", _reviewCandidates.Select(candidate =>
                $"{candidate.Path}:{candidate.WrittenUtcTicks}:{candidate.Size}:" +
                $"{_feedback.StatusOf(candidate.Path)}:" +
                $"{ReferenceEquals(candidate, _selectedCandidate)}"));
            if (signature == _reviewSignature)
            {
                return;
            }

            _reviewSignature = signature;
            _reviewGrid.Clear();

            _reviewHeader.text = _reviewCandidates.Count > 0
                ? $"Кандидаты на ревью ({_reviewCandidates.Count})"
                : "Кандидаты на ревью";

            if (_reviewCandidates.Count == 0)
            {
                var empty = new Label(
                    "Кандидатов нет. Выбери пул и нажми «Сгенерировать» — кадр " +
                    "появится здесь.");
                empty.AddToClassList("review-empty");
                _reviewGrid.Add(empty);
                return;
            }

            foreach (ReviewCandidate candidate in _reviewCandidates)
            {
                _reviewGrid.Add(MakeReviewTile(candidate));
            }
        }

        private VisualElement MakeReviewTile(ReviewCandidate candidate)
        {
            ArtStatus status = _feedback.StatusOf(candidate.Path);
            bool rejected = status == ArtStatus.Rejected;

            var tile = new VisualElement();
            tile.AddToClassList("review-tile");
            if (ReferenceEquals(candidate, _selectedCandidate))
            {
                tile.AddToClassList("review-tile-selected");
            }

            var holder = new VisualElement();
            holder.AddToClassList("review-tile-holder");

            var image = new Image
            {
                image = CandidateTexture(candidate.Path),
                // Полный кадр, а не обрезка: портрет 480×720 обязан быть виден
                // целиком — иначе лицо, ради которого кадр и делали, теряется.
                scaleMode = ScaleMode.ScaleToFit
            };
            image.AddToClassList("review-tile-image");
            holder.Add(image);

            // Клик по картинке = выбор: так панель справа рассказывает про
            // кандидата, но не решает его судьбу.
            ReviewCandidate captured = candidate;
            holder.AddManipulator(new Clickable(() => SelectCandidate(captured)));

            var name = new Label(candidate.DisplayName);
            name.AddToClassList("review-tile-name");

            var sub = new Label(candidate.StandId);
            sub.AddToClassList("review-tile-sub");

            var badge = new Label(rejected ? "брак" : "новый");
            badge.AddToClassList("frame-badge");
            badge.AddToClassList(rejected ? "badge-rejected" : "badge-candidate");

            var accept = new Button(() => AcceptCandidate(captured)) { text = "Принять в проект" };
            accept.AddToClassList("action");
            accept.AddToClassList("primary");
            accept.SetEnabled(!rejected);

            var reject = new Button(() => RejectCandidate(captured))
            {
                text = rejected ? "Снять брак" : "Брак"
            };
            reject.AddToClassList("action");
            reject.AddToClassList("reject");

            tile.Add(holder);
            tile.Add(name);
            tile.Add(sub);
            tile.Add(badge);
            tile.Add(accept);
            tile.Add(reject);
            return tile;
        }

        /// <summary>
        /// Принять кандидата из плитки: файл переносит в проект драйвер. Ровно
        /// то же действие, что и кнопка в правой панели, — но здесь оно под
        /// самим кадром, и решение не требует отдельного выбора.
        /// </summary>
        private void AcceptCandidate(ReviewCandidate candidate)
        {
            if (candidate == null || _client == null)
            {
                return;
            }

            SelectCandidate(candidate);
            _client.Approve(candidate.Path);
        }

        /// <summary>Пометка брака из плитки. Переключается: повторный клик её снимает.</summary>
        private void RejectCandidate(ReviewCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            if (_feedback.StatusOf(candidate.Path) == ArtStatus.Rejected)
            {
                _feedback.Remove(candidate.Path);
            }
            else
            {
                _feedback.Set(candidate.Path, ArtStatus.Rejected, _comment.value);
            }

            Persist();
            _reviewSignature = null;
            UpdateDriverUi();
            UpdateDetailPanel();
        }

        private static string KindTitle(ArtAssetKind kind)
        {
            return kind == ArtAssetKind.Portrait ? "Портреты" : "Фоны";
        }

        /// <summary>
        /// Короткое имя типа для дерева: «City» вместо «Портреты / City».
        /// Раздел «Портреты» теперь верхний уровень, и повторять его внутри
        /// незачем. Ключ группы — «&lt;вид&gt;|&lt;группа&gt;».
        /// </summary>
        private static string GroupShortTitle(CatalogGroup group)
        {
            string key = group.Key;
            int bar = key == null ? -1 : key.IndexOf('|');
            return bar >= 0 && bar + 1 < key.Length ? key.Substring(bar + 1) : group.Title;
        }

        /// <summary>
        /// Подпись левой панели. В неё входит всё, что видно в дереве: и
        /// каталог, и очередь, и выбор. Иначе панель не перерисовалась бы
        /// после пометки или скана.
        /// </summary>
        private string BuildLeftSignature()
        {
            var parts = new List<string>
            {
                _selectedPool == null
                    ? "-"
                    : $"{_selectedPool.Kind}|{_selectedPool.Group}|{_selectedPool.Name}",
                _selectedGroup?.Key ?? "-",
                _selectedCandidate?.Path ?? "-",
                _reviewMode ? "review" : "catalog"
            };

            foreach (CatalogGroup group in _model.Groups)
            {
                parts.Add($"{group.Key}:{group.FrameCount}");
                foreach (PoolInfo pool in group.Pools)
                {
                    int rejected = pool.Frames.Count(frame =>
                        _feedback.StatusOf(frame.AssetPath) == ArtStatus.Rejected);
                    int onReview = _reviewCandidates.Count(candidate =>
                        CandidateBelongsToPool(candidate, pool));
                    parts.Add($"{pool.Name}:{pool.Frames.Count}:{rejected}:{onReview}");
                }
            }

            foreach (ReviewCandidate candidate in _reviewCandidates)
            {
                parts.Add($"{candidate.Path}:{candidate.WrittenUtcTicks}:{candidate.Size}:" +
                          $"{_feedback.StatusOf(candidate.Path)}");
            }

            return string.Join("|", parts);
        }

        private void SelectPool(PoolInfo pool)
        {
            _selectedPool = pool;
            _selectedGroup = null;
            SetReviewMode(false);
        }

        /// <summary>
        /// Выбор группы целиком (кнопка «Все пулы группы»). <c>null</c> —
        /// вернуться к полному каталогу.
        ///
        /// Почему отдельный метод. Раньше эта кнопка звала <c>SelectPool(null)</c>,
        /// а <c>null</c> в фильтре значил «без фильтра» — то есть показывались
        /// кадры ВСЕХ групп. «Ничего не выбрано» и «выбрана группа» — разные
        /// состояния, и их нельзя выражать одним значением.
        /// </summary>
        private void SelectGroup(CatalogGroup group)
        {
            _selectedGroup = group;
            _selectedPool = null;
            SetReviewMode(false);
        }

        private void BuildFrameList(string keepAssetPath = null)
        {
            _visible.Clear();
            _pendingPreviews.Clear();

            // Три состояния дерева, и они разные: выбран пул — его кадры;
            // выбрана группа — кадры всех её пулов; не выбрано ничего — весь
            // каталог.
            foreach (CatalogGroup group in _model.Groups)
            {
                if (_selectedPool != null)
                {
                    if (!group.Pools.Any(pool => ReferenceEquals(pool, _selectedPool)))
                    {
                        continue;
                    }

                    _visible.AddRange(_selectedPool.Frames);
                    break;
                }

                if (_selectedGroup != null && !ReferenceEquals(group, _selectedGroup))
                {
                    continue;
                }

                foreach (PoolInfo pool in group.Pools)
                {
                    _visible.AddRange(pool.Frames);
                }
            }

            _frames.RefreshItems();

            _framesHeader.text = _selectedPool != null
                ? $"В проекте — {_selectedPool.Name} ({_visible.Count})"
                : _selectedGroup != null
                    ? $"В проекте — {_selectedGroup.Title} ({_visible.Count})"
                    : $"В проекте ({_visible.Count})";

            if (_visible.Count == 0)
            {
                if (_selectedCandidate == null)
                {
                    ShowDetails(null);
                }

                return;
            }

            // Выбор идёт через selectedIndex, а не через ShowDetails: иначе
            // событие выбора перезапишет панель. Кадр, который смотрели до
            // перестройки, остаётся выбранным — иначе после пометки брака
            // выбор прыгает на первый в списке и теряется из вида.
            int restored = keepAssetPath == null
                ? -1
                : _visible.FindIndex(entry => entry.AssetPath == keepAssetPath);

            _frames.ClearSelection();

            if (restored >= 0)
            {
                _frames.selectedIndex = restored;
            }
            else if (_selectedCandidate == null)
            {
                // Авто-выбор только когда ничего другого не открыто: иначе
                // осмотр кандидата сбрасывался бы на первом кадре списка.
                _frames.selectedIndex = 0;
            }
        }

        private VisualElement MakeFrameRow()
        {
            var frame = new VisualElement { name = "thumb" };
            frame.AddToClassList("frame-thumb-frame");

            // Кадр показывается ЦЕЛИКОМ и в своих пропорциях (2:3): раньше
            // это был квадрат с обрезкой по центру, и у портрета срезало голову.
            // USS-свойства для масштаба нет — в этой версии Unity оно
            // неизвестно и молча игнорируется.
            var image = new Image { name = "image", scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("frame-thumb");
            frame.Add(image);

            var name = new Label { name = "name" };
            name.AddToClassList("frame-name");

            var sub = new Label { name = "sub" };
            sub.AddToClassList("frame-sub");

            var text = new VisualElement { name = "text" };
            text.AddToClassList("frame-text");
            text.Add(name);
            text.Add(sub);

            var badge = new Label { name = "badge" };
            badge.AddToClassList("frame-badge");

            var row = new VisualElement { name = "row" };
            row.AddToClassList("frame-row");
            row.Add(frame);
            row.Add(text);
            row.Add(badge);

            return row;
        }

        private void BindFrameRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _visible.Count)
            {
                return;
            }

            CatalogEntry entry = _visible[index];
            element.userData = entry;

            element.Q<Label>("name").text = $"{entry.Pool}_{entry.Id.VariantNumber}";
            element.Q<Label>("sub").text = entry.CanRegenerate
                ? entry.StandId
                : "нет id стенда";

            Label badge = element.Q<Label>("badge");
            ArtStatus status = _feedback.StatusOf(entry.AssetPath);
            badge.text = StatusText(status);
            badge.ClearClassList();
            badge.AddToClassList("frame-badge");
            badge.AddToClassList(StatusClass(status));

            Image image = element.Q<Image>("image");
            image.image = null;

            _pendingPreviews.Add(element);
        }

        private void PollPreviews()
        {
            for (int i = _pendingPreviews.Count - 1; i >= 0; i--)
            {
                VisualElement element = _pendingPreviews[i];

                if (element.panel == null || !(element.userData is CatalogEntry entry))
                {
                    _pendingPreviews.RemoveAt(i);
                    continue;
                }

                Image image = element.Q<Image>("image");
                if (image.image != null)
                {
                    _pendingPreviews.RemoveAt(i);
                    continue;
                }

                Texture2D preview = PreviewFor(entry);
                if (preview != null)
                {
                    image.image = preview;
                    _pendingPreviews.RemoveAt(i);
                }
            }
        }

        private Texture2D PreviewFor(CatalogEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (_previews.TryGetValue(entry.AssetPath, out Texture2D cached))
            {
                return cached;
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.AssetPath);
            if (texture == null)
            {
                return null;
            }

            Texture2D preview = AssetPreview.GetAssetPreview(texture);
            if (preview != null)
            {
                _previews[entry.AssetPath] = preview;
            }

            return preview;
        }

        /// <summary>
        /// Превью для панели деталей — полная текстура, а не мелкий
        /// <see cref="AssetPreview"/>. Кадр диалога показывается крупно, и
        /// уменьшенная копия на этом размере выглядит мутной: у AssetPreview
        /// потолок ~128 px, у кадра — 480×720. Read/Write при этом не нужен:
        /// текстура отдаётся GPU как есть, из неё ничего не читается на CPU.
        /// Кэшируем только последний открытый кадр, иначе 144 несжатых
        /// текстуры съедят ~200 МБ.
        /// </summary>
        private Texture2D DetailPreviewFor(CatalogEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (_detailTexturePath == entry.AssetPath && _detailTexture != null)
            {
                return _detailTexture;
            }

            _detailTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry.AssetPath);
            _detailTexturePath = entry.AssetPath;
            return _detailTexture;
        }

        private void OnFrameSelected(IEnumerable<object> selection)
        {
            int index = _frames.selectedIndex;
            ShowDetails(index >= 0 && index < _visible.Count ? _visible[index] : null);
        }

        private void ShowDetails(CatalogEntry entry)
        {
            if (entry != null)
            {
                // Кадр проекта и кандидат — взаимоисключающий выбор: панель
                // показывает ровно один объект, иначе непонятно, к чему
                // относится нажатая кнопка.
                _selectedCandidate = null;
            }

            _selected = entry;

            // Клик по кадру НЕ переключает вкладку. Раньше возвращал на
            // «Объект»: мол, человек выбрал кадр, значит надо его показать.
            // На деле это мешало — щёлкаешь по кадрам, пока смотришь промпты
            // или прогресс, и каждый раз выкидывает на другую вкладку.
            // Выбор обновляет содержимое панели объекта, а вкладкой человек
            // управляет сам.
            UpdateDetailPanel();
        }

        /// <summary>
        /// Правая панель показывает ровно один объект и НАЗЫВАЕТ его.
        ///
        /// Почему так. Раньше в одной колонке жили действия кадра («Одобрить»,
        /// «Пометить брак»), пула («Сгенерировать») и проекта («Доп. места»),
        /// а у кандидата свои кнопки стояли в списке слева. Два набора
        /// «принять/отклонить» выглядели дублем, хотя означали разное:
        /// кандидат переносят в проект, у кадра только ставят отметку. Теперь
        /// заголовок называет объект, действия кандидата и кадра показываются
        /// взаимоисключающе, а действия пула и проекта уехали в свои разделы.
        /// </summary>
        private void UpdateDetailPanel()
        {
            bool hasCandidate = _selectedCandidate != null;
            bool hasFrame = _selected != null;

            _candidateActions.style.display = hasCandidate ? DisplayStyle.Flex : DisplayStyle.None;
            _frameActions.style.display = hasFrame ? DisplayStyle.Flex : DisplayStyle.None;

            ArtStatus status;
            string comment;

            if (hasCandidate)
            {
                ReviewCandidate candidate = _selectedCandidate;
                status = _feedback.StatusOf(candidate.Path);
                comment = _feedback.Get(candidate.Path)?.Comment ?? string.Empty;

                _detailHeader.text = "Кандидат · ещё не в проекте";
                _frameTitle.text = candidate.DisplayName;
                _frameId.text = candidate.StandId;
                _framePath.text = candidate.Path;
                _frameStatus.text = status == ArtStatus.Rejected
                    ? "помечен браком — уйдёт на перегенерацию"
                    : "ждёт решения";

                // Покрытие — метрика портрета; у фона она всегда 100 и не
                // значит ничего, поэтому у фона строки нет.
                _frameCoverage.text = candidate.HasCoverage
                    ? $"покрытие {candidate.Coverage}% · норма 41–62%"
                    : string.Empty;

                _rejectCandidate.text = status == ArtStatus.Rejected
                    ? "Снять пометку"
                    : "Отклонить и перегенерировать";
                _accept.SetEnabled(status != ArtStatus.Rejected);

                _preview.image = CandidateTexture(candidate.Path);
            }
            else if (hasFrame)
            {
                CatalogEntry entry = _selected;
                status = _feedback.StatusOf(entry.AssetPath);
                comment = _feedback.Get(entry.AssetPath)?.Comment ?? string.Empty;

                _detailHeader.text = "Кадр проекта";
                _frameTitle.text = $"{entry.Pool}_{entry.Id.VariantNumber}";
                _frameId.text = entry.CanRegenerate
                    ? entry.StandId
                    : "id стенда неизвестен — каталог стенда недоступен";
                _framePath.text = entry.AssetPath;
                _frameStatus.text = $"отметка: {StatusText(status)}";
                _frameCoverage.text = string.Empty;

                _approve.text = status == ArtStatus.Approved
                    ? "Снять отметку «принят»"
                    : "Отметить принятым";
                _reject.text = status == ArtStatus.Rejected
                    ? "Снять пометку брака"
                    : "Пометить браком";

                _preview.image = DetailPreviewFor(entry);
            }
            else
            {
                _detailHeader.text = "Ничего не выбрано";
                _frameTitle.text = "—";
                _frameId.text = "—";
                _framePath.text = "—";
                _frameStatus.text = "выбери кандидата или кадр в списке";
                _frameCoverage.text = string.Empty;
                _rejectComment.style.display = DisplayStyle.None;
                _comment.SetValueWithoutNotify(string.Empty);
                _preview.image = null;
                LayoutPreview();
                return;
            }

            // Комментарий нужен только браку: без пометки он никуда не идёт.
            bool rejected = status == ArtStatus.Rejected;
            _rejectComment.style.display = rejected ? DisplayStyle.Flex : DisplayStyle.None;
            _comment.SetEnabled(rejected);
            _comment.SetValueWithoutNotify(rejected ? comment : string.Empty);

            LayoutPreview();
        }

        private void OnPreviewHolderResized(GeometryChangedEvent evt)
        {
            // Скрытая страница объекта даёт нулевую высоту: это не замер, а
            // отсутствие раскладки. Запомнить его — значит потерять ширину
            // правого блока и вернуть его узким после закрытия генератора.
            if (evt.newRect.height > 1f)
            {
                _previewBox = evt.newRect;
            }

            LayoutPreview();
        }

        /// <summary>
        /// Окно изменило размер — кадр и блок пересчитываются целиком.
        ///
        /// Подписка именно на окно, а не только на место под кадром: высота
        /// окна задаёт ширину кадра, и пропущенный пересчёт оставляет блок
        /// широким, а кадр — маленьким. Место под кадром меняется не всегда
        /// (например, в узком окне блок уже упёрся в потолок), а окно — всегда.
        /// </summary>
        private void OnWindowResized(GeometryChangedEvent evt)
        {
            LayoutPreview();
        }

        /// <summary>
        /// Раскладка кадра и правого блока — один проход, одно вычисление.
        ///
        /// Оба числа считает <see cref="PreviewFit"/> из одной ширины кадра,
        /// поэтому разойтись они не могут. Раньше это были два независимых
        /// расчёта по раскладке прошлого кадра: блок успевал уехать позже,
        /// а между ним и кадром оставалась полоса.
        /// </summary>
        private void LayoutPreview()
        {
            if (_rightPanel == null || _previewHolder == null || _preview == null)
            {
                return;
            }

            float boxHeight = _previewHolder.layout.height;
            if (boxHeight <= 1f)
            {
                // Раскладки ещё нет (первый кадр окна) — берём последний замер.
                boxHeight = _previewBox.height;
            }

            (float sourceWidth, float sourceHeight) = SourceSize();
            PreviewFit.Result fit = PreviewFit.Compute(
                sourceWidth,
                sourceHeight,
                boxHeight,
                AvailableWidth(),
                RightPanelChrome,
                PreviewFit.MinPanelWidth,
                PreviewFit.MaxPanelWidth);

            if (!fit.IsValid)
            {
                return;
            }

            // Сравниваем с тем, что выставили сами, а не с resolvedStyle:
            // разрешённая ширина честно отстаёт на кадр, и сверка с ней
            // заставляла бы писать одно и то же значение повторно.
            if (Mathf.Abs(fit.PanelWidth - _appliedPanelWidth) >= 1f)
            {
                _appliedPanelWidth = fit.PanelWidth;
                _rightPanel.style.width = fit.PanelWidth;
            }

            if (Mathf.Abs(fit.PreviewWidth - _appliedPreviewWidth) < 1f &&
                Mathf.Abs(fit.PreviewHeight - _appliedPreviewHeight) < 1f)
            {
                return;
            }

            _appliedPreviewWidth = fit.PreviewWidth;
            _appliedPreviewHeight = fit.PreviewHeight;
            _preview.style.width = fit.PreviewWidth;
            _preview.style.height = fit.PreviewHeight;
        }

        /// <summary>
        /// Сколько ширины окно может отдать правому блоку: ширина окна минус
        /// левая колонка и минимум центральной.
        ///
        /// Величина обязана быть независимой от самого блока. Ширина места под
        /// кадр зависит от ширины блока, и если считать блок от неё, задача
        /// замыкается: блок застревает на той ширине, с которой начал.
        /// Ноль — «неизвестно»: тогда ширину задаёт только высота.
        /// </summary>
        private float AvailableWidth()
        {
            if (_leftPanel == null || _centerPanel == null)
            {
                return 0f;
            }

            float windowWidth = rootVisualElement.layout.width;
            float leftWidth = _leftPanel.resolvedStyle.width;
            float centerMinWidth = _centerPanel.resolvedStyle.minWidth.value;

            // Сравнения «больше» отсекают и NaN, и нули: значения приходят из
            // USS и до первого кадра могут быть не разрешены.
            if (!(windowWidth > 1f) || !(leftWidth > 1f) || !(centerMinWidth > 1f))
            {
                return 0f;
            }

            return windowWidth - leftWidth - centerMinWidth;
        }

        /// <summary>
        /// Размер, пропорцию которого обязан держать кадр: у портрета
        /// 480×720, у фона 856×480. Берётся у самого изображения, а не задан
        /// числом, — иначе фон показывался бы в рамке портрета и обрезался.
        /// </summary>
        private (float Width, float Height) SourceSize()
        {
            Texture texture = _preview?.image;
            if (texture != null && texture.width > 0 && texture.height > 0)
            {
                return (texture.width, texture.height);
            }

            return (PortraitWidth, PortraitHeight);
        }

        // ------------------------------------------------------------------- действия

        /// <summary>
        /// Отметка кадра проекта. Это НЕ то же, что «принять кандидата»: файл
        /// уже лежит в проекте, отметить — значит записать решение в журнал
        /// ревью. Кнопка переключается, чтобы отметку можно было снять.
        ///
        /// Перегенерация помеченного — отдельная кнопка внизу окна: помечают
        /// несколько разных кадров, а прогон запускают один раз на все.
        /// </summary>
        private void ApproveSelected()
        {
            if (_selected == null)
            {
                return;
            }

            if (_feedback.StatusOf(_selected.AssetPath) == ArtStatus.Approved)
            {
                _feedback.Remove(_selected.AssetPath);
            }
            else
            {
                _feedback.Set(_selected.AssetPath, ArtStatus.Approved, null);
            }

            Persist();
            ReapplyFilter();
            UpdateDetailPanel();
        }

        private void RejectSelected()
        {
            if (_selected == null)
            {
                return;
            }

            if (_feedback.StatusOf(_selected.AssetPath) == ArtStatus.Rejected)
            {
                _feedback.Remove(_selected.AssetPath);
            }
            else
            {
                _feedback.Set(_selected.AssetPath, ArtStatus.Rejected, _comment.value);
            }

            Persist();
            ReapplyFilter();
            UpdateDetailPanel();
        }

        private void SaveComment()
        {
            // Комментарий пишется и кандидату, и кадру проекта: ключ — путь
            // того объекта, который сейчас открыт в панели.
            string key = _selectedCandidate?.Path ?? _selected?.AssetPath;
            if (key == null)
            {
                return;
            }

            ArtStatus status = _feedback.StatusOf(key);
            if (status != ArtStatus.Rejected)
            {
                // Комментарий без пометки брака ни на что не влияет: он
                // сохранится в тот момент, когда объект помечен браком.
                return;
            }

            _feedback.Set(key, status, _comment.value);
            Persist();
            ReapplyFilter();
            RenderLeftPanel();
        }

        private void OpenSelectedFolder()
        {
            if (_selected == null)
            {
                return;
            }

            // EditorUtility.RevealFolder в Unity 6 убран. Показать папку в
            // окне Project — это выделить её и пингнуть: выделение открывает
            // Project window, пинг раскрывает дерево до папки.
            string folder = Path.GetDirectoryName(_selected.AssetPath)?.Replace('\\', '/');
            DefaultAsset asset = string.IsNullOrEmpty(folder)
                ? null
                : AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);

            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void Persist()
        {
            _feedback.Save(ArtStudioPaths.Feedback(_projectRoot));
        }

        private void WriteStatus(IReadOnlyList<string> extraProblems = null)
        {
            // Один счётчик на окно: раньше число кадров стояло и в заголовке
            // списка, и здесь — «Кадры (144)» против «всего кадров: 144».
            int marked = _feedback?.Rejected().Count() ?? 0;
            _status.text =
                $"на ревью: {_reviewCandidates.Count}   " +
                $"в проекте: {_model.Total}   " +
                $"принято: {_model.Approved}   " +
                $"не смотрели: {_model.New}   " +
                $"к перегенерации: {marked}";

            var problems = new List<string>();
            if (extraProblems != null)
            {
                problems.AddRange(extraProblems);
            }

            problems.AddRange(_reviewProblems);
            problems.AddRange(_extraProblems);
            problems.AddRange(_settings.FindSetupProblems());
            AddDriverProblems(problems);

            _problems.text = problems.Count == 0
                ? string.Empty
                : string.Join("\n", problems.Take(3).ToList()) +
                  (problems.Count > 3 ? $"\n…и ещё {problems.Count - 3}" : string.Empty);
        }

        private void AddDriverProblems(List<string> problems)
        {
            if (_client == null)
            {
                problems.Add("драйвер арт-студии не подключён");
                return;
            }

            if (!_client.HasDriverScript)
            {
                problems.Add($"драйвер не найден: {_client.DriverScriptPath}");
            }
            else if (!_client.State.DriverConnected)
            {
                problems.Add("драйвер не отвечает — нажми «Драйвер: поднять»");
            }
            else if (_client.State.ProtocolMismatch)
            {
                problems.Add($"версия протокола драйвера {_client.State.DriverProtocol} " +
                             $"≠ {ArtProtocol.Version}: обнови драйвер");
            }

            problems.AddRange(_client.Problems);

            if (!string.IsNullOrEmpty(_client.State.LastError))
            {
                problems.Add($"драйвер: {_client.State.LastError}");
            }
        }

        // ----------------------------------------------------------------- драйвер

        private void SetupDriver()
        {
            _client = new ArtDriverClient(_projectRoot, _settings);

            if (!_client.HasDriverScript)
            {
                // Строка проблем объяснит это человеку; молча ничего не делаем.
                return;
            }

            _client.StartDriver();
            _client.Ping();
            _client.RequestCatalog();
        }

        /// <summary>
        /// Одна кнопка на драйвер и ComfyUI.
        ///
        /// ПОЧЕМУ ОДНА. Драйвер и сервер — не два решения, а два шага одного:
        /// без сервера драйвер не нарисует кадр, без драйвера сервер никто не
        /// спросит. Две кнопки требовали помнить порядок нажатий и допускали
        /// половинчатое состояние «драйвер поднят, сервер забыт». Порядок
        /// знает кнопка.
        ///
        /// ПОДЪЁМ. Сначала драйвер, потом `server_start`: ComfyUI поднимает
        /// драйвер и владеет его жизненным циклом — окно не должно запускать
        /// сервер в обход. Пока идёт переход, кнопка ПОГАШЕНА и называет фазу:
        /// иначе второе нажатие запускало бы второй подъём поверх первого.
        ///
        /// ОСТАНОВКА. <see cref="ArtDriverClient.StopEverything"/> гасит драйвер
        /// (тот сам снимает ComfyUI деревом процессов) и дополнительно
        /// проверяет порт: если сервер пережил драйвер, его добивают по
        /// маркеру. Требование «чтобы не висело процессов» — не косметика:
        /// осиротевший ComfyUI держит 10 ГБ VRAM, и следующий прогон встаёт к
        /// нему в очередь (README стенда: «60+ с/шаг»).
        /// </summary>
        private void ToggleStand()
        {
            if (_client == null)
            {
                SetupDriver();
                PollDriver();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            _client.ClearProblems();

            if (_standPhase == StandPhase.Up)
            {
                _client.StopEverything();
                _standPhase = StandPhase.Stopping;
                _standDeadline = StandControl.Deadline(StandPhase.Stopping, now);
            }
            else
            {
                // `StartDriver` идемпотентен: живой драйвер он не трогает и не
                // сбрасывает состояние — поднимается только то, чего нет.
                _client.StartDriver();
                _client.Ping();
                _client.RequestCatalog();
                _client.ServerStart();
                _standPhase = StandPhase.Starting;
                _standDeadline = StandControl.Deadline(StandPhase.Starting, now);
            }

            PollDriver();
        }

        /// <summary>
        /// Перегенерация помеченного брака. Список кадров окно не присылает:
        /// марки лежат в журнале ревью, а какие stand-id за ними стоят — знает
        /// драйвер, у него каталог стенда (см. `regen_marked` в спеке).
        /// </summary>
        private void RegenerateMarked()
        {
            _client.RegenerateMarked();
        }

        /// <summary>
        /// Показать или спрятать область доп. мест. Нужна она редко, а
        /// раскрытая отнимает высоту у кадра — поэтому по умолчанию закрыта и
        /// открывается кнопкой в нижней строке, а не висит всегда.
        /// </summary>
        private void TogglePlaces()
        {
            bool hidden = _placesPanel.resolvedStyle.display == DisplayStyle.None;
            _placesPanel.style.display = hidden ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Добавить выбранному пулу N кадров и сразу их сгенерировать.
        ///
        /// Почему буквы считает окно. Буква варианта — это номер кадра в
        /// проекте (`a` → 01), и человеку её знать незачем. Раньше он вписывал
        /// буквы в поле «буквы» в левой панели, и было непонятно, что это и к
        /// чему относится; теперь окно берёт следующие свободные буквы само, а
        /// человек говорит только «сколько».
        ///
        /// Если у пула уже есть несделанные кадры (ростер дал больше букв, чем
        /// разложено), в работу идут сначала они — файл доп. кадров не
        /// трогается.
        /// </summary>
        private void AddVariants()
        {
            string prefix = SelectedPoolPrefix();
            if (_client == null || prefix == null)
            {
                return;
            }

            if (!prefix.StartsWith("p_", StringComparison.Ordinal))
            {
                // У фона нет буквы-кадра: номер всегда 01, поэтому «добавить
                // кадр» фону — это новое место, а не новая буква.
                _extraProblems.Clear();
                _extraProblems.Add($"доп. кадры: «{prefix}» — не портрет; у фона новый кадр — "
                                   + "это новое место (кнопка «Доп. места» внизу окна)");
                WriteStatus(_loadProblems);
                return;
            }

            int count = Mathf.Max(1, _addCount.value);
            int missing = Mathf.Max(0, count - AvailableIds(_client.State.FindPool(prefix)));

            if (missing > 0)
            {
                string letters = ExtraPoolsFile.NextFreeLetters(UsedLetters(prefix), missing);
                if (letters.Length < missing)
                {
                    _extraProblems.Clear();
                    _extraProblems.Add($"доп. кадры: у пула {prefix} свободных букв осталось "
                                       + $"{letters.Length}, а просили {missing}");
                    WriteStatus(_loadProblems);
                    return;
                }

                _extraPools.Add(prefix, letters);
                _extraPools.Save(ArtStudioPaths.ExtraPools(_projectRoot));

                // Файл читает стенд: без запроса каталога новых кадров у пула
                // ещё нет, и генерация ответила бы отказом.
                _client.RequestCatalog();
            }

            _extraProblems.Clear();
            _client.Generate(prefix, count);
            WriteStatus(_loadProblems);
        }

        /// <summary>
        /// Принять кандидата: файл переносит в проект драйвер. Это
        /// единственное действие, которое физически меняет арт проекта.
        /// </summary>
        private void AcceptSelectedCandidate()
        {
            if (_selectedCandidate == null)
            {
                return;
            }

            _client?.Approve(_selectedCandidate.Path);
        }

        /// <summary>
        /// Брак на кандидата. Ключ — путь кандидата в <c>review/</c>, а не
        /// кадра проекта: по нему драйвер находит id стенда и перегенерирует
        /// ТОТ ЖЕ кадр («ещё раз того же человека»), тогда как кадр в проекте
        /// получил бы следующий свободный номер.
        ///
        /// Пометка переключается: снять её можно, иначе случайный клик стоил
        /// бы лишнего прогона GPU — а он длится минуты.
        /// </summary>
        private void RejectSelectedCandidate()
        {
            RejectCandidate(_selectedCandidate);
        }

        private string SelectedPoolPrefix()
        {
            return PrefixOf(_selectedPool);
        }

        /// <summary>
        /// Префикс id стенда для пула проекта. Правило одно на всё окно: им
        /// пользуются и «Сгенерировать», и сопоставление кандидатов с пулом —
        /// иначе кандидат мог бы попасть не в тот пул дерева.
        /// </summary>
        private static string PrefixOf(PoolInfo pool)
        {
            if (pool == null)
            {
                return null;
            }

            return StandGroupMapper.TryMap(pool.Kind, pool.Group, out string group)
                ? DriverPool.PrefixFor(pool.Kind, group, pool.Name)
                : null;
        }

        /// <summary>
        /// Кандидат принадлежит пулу дерева. Сопоставление идёт по префиксу
        /// id стенда, а не по имени: «Forge» есть и в городе, и в деревне, и
        /// сравнение по имени отнесло бы кандидата не в тот пул.
        /// </summary>
        private static bool CandidateBelongsToPool(ReviewCandidate candidate, PoolInfo pool)
        {
            string prefix = PrefixOf(pool);
            if (prefix == null || candidate == null)
            {
                return false;
            }

            return candidate.Kind == pool.Kind
                   && (candidate.StandId == prefix
                       || candidate.StandId.StartsWith(prefix + "_", StringComparison.Ordinal));
        }

        private void PollDriver()
        {
            if (_client == null)
            {
                return;
            }

            _client.Poll();

            // Драйвер обслуживает только команды, написанные после его старта,
            // а окно отправляет `ping` и `catalog` сразу после запуска процесса:
            // их могло не быть в файле в момент старта. Поэтому, как только
            // связь появилась, просим каталог ещё раз — иначе панель пула
            // навсегда осталась бы с «нет данных стенда».
            if (_client.State.DriverConnected && !_wasDriverConnected)
            {
                _client.Ping();
                _client.RequestCatalog();

                // `server_start` летит тем же порядком и теряется так же:
                // кнопка «Стенд» поднимает драйвер и сразу просит сервер, а
                // команда ложится в файл раньше, чем драйвер откроет чтение.
                // Без повтора сервер не поднимался ВООБЩЕ, и кнопка навсегда
                // оставалась серой «Поднимается: сервер…» — проверено живьём.
                if (_standPhase == StandPhase.Starting)
                {
                    _client.ServerStart();
                }
            }

            _wasDriverConnected = _client.State.DriverConnected;

            if (_client.State.Jobs.Running != null)
            {
                _client.State.Jobs.Tick(0.15);
            }

            if (_client.State.AcknowledgeReviewChanged())
            {
                RescanReview();
            }

            List<string> approved = _client.State.TakeApprovedPaths();
            if (approved.Count > 0)
            {
                // Драйвер положил кадр в Assets — без обновления Unity его не увидит.
                AssetDatabase.Refresh();
                LoadState();

                // Человек кадр принял — значит он принят и в журнале. Иначе
                // кадр попадал в проект «не смотренным», и его пришлось бы
                // отмечать второй раз той же кнопкой.
                foreach (string path in approved)
                {
                    _feedback.Set(path, ArtStatus.Approved, null);
                }

                Persist();
                ReapplyFilter();
            }

            UpdateDriverUi();
        }

        private void UpdateDriverUi()
        {
            ArtDriverState state = _client.State;

            UpdateStandButton(state);

            // Промпты разбираются только когда стенд ответил новым: сам он их
            // не шлёт, а JSON большой и разбирать его на каждом опросе — впустую.
            UpdatePrompts(state);

            // Пока идёт прогон, плитка добавления уступает место прогрессу.
            // Заодно это единственное место, которое знает, что генерация
            // жива, — им же гасится «Перегенерировать» ниже.
            UpdateGeneration(state);
            UpdateStatusBar(state);

            // Прогон идёт — кнопка погашена: второй `generate` поверх первого
            // работу не ускоряет, а складывается с ней.
            _addButton.SetEnabled(
                state.DriverConnected && SelectedPoolPrefix() != null && !_generationRunning);

            // В подсказке — размер пула, а не «свободно N»: свободных может
            // быть ноль (всё уже сделано или ждёт ревью), и это читалось бы
            // как «пул полон», хотя кнопка работает — она заведёт новые буквы.
            DriverPool selected = state.FindPool(SelectedPoolPrefix());
            _addHint.text = selected == null
                ? "новые варианты этого пула"
                : $"новые варианты · в пуле уже {selected.All.Count}";

            // Сколько помечено браком — считает журнал ревью, а не окно: одни
            // и те же правила в тесте и на экране. Кнопка молчит, когда
            // перегенерировать нечего: мёртвая кнопка хуже её отсутствия.
            int rejected = _feedback?.Rejected().Count() ?? 0;
            _regenButton.text = rejected > 0
                ? $"Перегенерировать помеченное ({rejected})"
                : "Перегенерировать помеченное";
            _regenButton.SetEnabled(
                state.DriverConnected && rejected > 0 && !_generationRunning);

            RenderExtraPlaces();
            RenderLeftPanel();

            WriteStatus(_loadProblems);
        }

        /// <summary>
        /// Кнопка «Стенд»: надпись, доступность и переходы автомата.
        ///
        /// Срок перехода проверяется здесь, а не отдельным таймером: опрос идёт
        /// раз в 150 мс, и этого достаточно, чтобы заметить «не поднялось за
        /// 180 с». Отдельный таймер пришлось бы останавливать вручную при
        /// каждом переходе — и однажды забыть.
        /// </summary>
        private void UpdateStandButton(ArtDriverState state)
        {
            bool driverAlive = _client.IsDriverAlive;
            bool serverReady = IsServerReady(state.ServerState);
            double now = EditorApplication.timeSinceStartup;

            StandPhase resolved = StandControl.Resolve(
                _standPhase, driverAlive, serverReady, now, _standDeadline);

            if (resolved != _standPhase)
            {
                // Сорвавшийся переход обязан назвать причину: молча вернуться
                // в «не поднят» значит соврать, что ничего и не начиналось.
                string problem = StandControl.TimeoutProblem(
                    _standPhase, now, _standDeadline);
                if (!string.IsNullOrEmpty(problem))
                {
                    _loadProblems.Add(problem);
                }

                _standPhase = resolved;
                if (resolved == StandPhase.Down || resolved == StandPhase.Up)
                {
                    _standDeadline = 0;
                }
            }

            SetText(_standButton, StandControl.Label(
                _standPhase, driverAlive, state.ServerState));
            _standButton.SetEnabled(StandControl.CanPress(_standPhase));
        }

        private static bool IsServerReady(string serverState)
        {
            return !string.IsNullOrEmpty(serverState) && serverState.Contains("готов");
        }

        /// <summary>
        /// Прогресс генерации — ЕДИНСТВЕННОЕ место, где он рисуется.
        ///
        /// ПОЧЕМУ ДВЕ ПОЛОСЫ. Одна не отвечает на вопрос «сколько осталось».
        /// На заявке из четырёх кадров общий процент идёт рывками по 25, а на
        /// одном кадре совпадает с кадровым — то есть либо не движется, либо
        /// не говорит ничего нового. Поэтому полос две: ВСЯ заявка (сколько
        /// кадров готово) и ТЕКУЩИЙ кадр (что происходит прямо сейчас). Числа
        /// стоят в подписях над полосами: полоса без числа заставляет гадать.
        ///
        /// ПОЧЕМУ ПРОЦЕНТ КАДРА НЕ РАВЕН ПРОЦЕНТУ ШАГОВ. Стенд шлёт только
        /// шаги сэмплера, а подготовка кадра (загрузка 10 ГБ весов, энкодинг)
        /// и постобработка (декод, запись, проверка) событий не дают вовсе —
        /// а на холодном сервере подготовка одна занимает десятки секунд.
        /// Шкала кадра разбита на три полосы, и в крайних значение растёт по
        /// времени: см. <see cref="GenerationProgress.FramePercent"/>.
        ///
        /// ЗАЧЕМ БЕГУЩАЯ ПОДСВЕТКА. Загрузка моделей занимает до 410 с и не
        /// даёт ни одного события прогресса — по логу стенда это самая долгая
        /// операция прогона. Стоящая полоса в это время неотличима от
        /// зависания, поэтому при молчании прогресса включается движение
        /// (см. <see cref="GenerationProgress.StaleSeconds"/>). Подсветка идёт
        /// по полосе КАДРА: молчит именно кадр, а не заявка.
        ///
        /// Плитка добавления на время прогона ГАСНЕТ, но остаётся на месте:
        /// второй `generate` поверх первого работу не ускоряет, а складывается
        /// с ней (очередь стенда линейная). Это не косметика — кнопка, которая
        /// такое позволяет, ловушка.
        ///
        /// Переход «идёт → готово» держится на экране небольшую задержку
        /// (<see cref="_generationLingerUntil"/>): опрос идёт раз в 150 мс, и без
        /// неё готовый результат мелькнул бы и исчез.
        /// </summary>
        /// <summary>
        /// Статус кадра для списка заявки. Короткий и однозначный: человек
        /// должен отличить «рисуется» от «нарисован, ждёт приёмки», а эти
        /// два состояния на полосах выглядят почти одинаково.
        /// </summary>
        private static string JobStateText(GenerationJob job, bool isCurrent)
        {
            if (job.State == JobState.Done)
            {
                return "готов";
            }

            if (job.State == JobState.Failed)
            {
                return "не прошёл проверку";
            }

            if (job.State == JobState.Cancelled)
            {
                return "отменён";
            }

            if (job.Drawn)
            {
                return "нарисован, ждёт приёмки";
            }

            if (isCurrent)
            {
                return $"рисует · {GenerationProgress.FramePercent(job, 0)}%";
            }

            return "ждёт";
        }

        private static string JobStateClass(GenerationJob job, bool isCurrent)
        {
            if (job.State == JobState.Done)
            {
                return "job-row-done";
            }

            if (job.State == JobState.Failed || job.State == JobState.Cancelled)
            {
                return "job-row-failed";
            }

            return isCurrent ? "job-row-current" : null;
        }

        /// <summary>
        /// Перестраивает список кадров заявки. Узлов мало (единицы), а опрос
        /// идёт раз в 150 мс, поэтому пересборка дешевле, чем обновлять
        /// существующие строки поштучно: список и так меняется на каждом шаге
        /// сэмплера.
        /// </summary>
        private void RenderJobList(JobQueue jobs, GenerationJob current)
        {
            if (_jobList == null)
            {
                return;
            }

            _jobList.Clear();

            for (int i = 0; i < jobs.Jobs.Count; i++)
            {
                GenerationJob job = jobs.Jobs[i];
                bool isCurrent = ReferenceEquals(job, current);

                var row = new VisualElement();
                row.AddToClassList("job-row");

                string stateClass = JobStateClass(job, isCurrent);
                if (stateClass != null)
                {
                    row.AddToClassList(stateClass);
                }

                var dot = new VisualElement();
                dot.AddToClassList("job-dot");
                row.Add(dot);

                var name = new Label(GenerationProgress.JobLabel(job));
                name.AddToClassList("job-name");
                row.Add(name);

                var state = new Label(JobStateText(job, isCurrent));
                state.AddToClassList("job-state");
                row.Add(state);

                _jobList.Add(row);
            }
        }

        private void UpdateGeneration(ArtDriverState state)
        {
            JobQueue jobs = state.Jobs;
            bool hasJobs = jobs.Total > 0;
            _generationRunning = hasJobs && !jobs.IsIdle;
            bool justStarted = _generationRunning && !_wasGenerating;

            if (_generationRunning)
            {
                _wasGenerating = true;
                _generationLingerUntil = 0;
            }
            else if (_wasGenerating)
            {
                _wasGenerating = false;
                _generationLingerUntil = EditorApplication.timeSinceStartup + 8.0;
            }

            bool show = _generationRunning
                || (_generationLingerUntil > 0
                    && EditorApplication.timeSinceStartup < _generationLingerUntil);

            if (!show)
            {
                _generationLingerUntil = 0;
                if (hasJobs && jobs.IsIdle)
                {
                    // Убираем завершённые: иначе следующая заявка начнёт считать
                    // «всего» с прошлого прогона и общий процент соврёт.
                    jobs.ClearFinished();
                }
            }

            // Плитка добавления видна только у выбранного пула и только когда
            // прогона нет; во время прогона она есть, но погашена.
            _addRow.style.display = _selectedPool == null
                ? DisplayStyle.None
                : DisplayStyle.Flex;

            GenerationJob current = jobs.Running;

            // Список кадров: без него четыре одинаковых процента полос не
            // отвечают на вопрос «что рисуется, а что ждёт».
            RenderJobList(jobs, current);

            // Смена подписи прогресса = работа сдвинулась. Это же единственный
            // способ отличить «считает шаг» от «молчит 400 секунд».
            string signature = current == null
                ? null
                : $"{current.StandId}:{current.Percent}:{current.ProgressText}";
            if (signature != _progressSignature)
            {
                _progressSignature = signature;
                _progressChangedAt = EditorApplication.timeSinceStartup;
            }

            double stallSeconds = EditorApplication.timeSinceStartup - _progressChangedAt;
            int overall = GenerationProgress.Overall(jobs, current, stallSeconds);
            int frame = GenerationProgress.FramePercent(current, stallSeconds);

            // ТРЕТЬЯ ПОЛОСА — ЭТАП. Этапы прогона (вырезка, проверка, приёмка)
            // важнее этапа кадра: именно они раньше шли молча минутами.
            if (justStarted)
            {
                state.ResetStandStage();
            }

            string stageSignature = null;
            if (!string.IsNullOrEmpty(state.StandStage))
            {
                stageSignature =
                    $"run:{state.StandStage}:{state.StandStageDone}/{state.StandStageTotal}";
            }
            else if (current != null && !string.IsNullOrEmpty(current.Stage))
            {
                stageSignature = $"job:{current.Stage}:{current.Percent}";
            }

            if (stageSignature != _stageSignature)
            {
                _stageSignature = stageSignature;
                _stageChangedAt = EditorApplication.timeSinceStartup;
            }

            // Смена кадра обнуляет ВИДИМОЕ значение, а не настоящее.
            // Анимация нужна внутри кадра; перенести проценты предыдущего на
            // новый значит показать «почти готов» до начала работы над ним.
            string frameId = current?.StandId;
            if (!string.Equals(_shownFrameId, frameId, StringComparison.Ordinal))
            {
                _shownFrameId = frameId;
                _shownFrame = 0f;
            }

            if (!string.Equals(_shownStageKey, stageSignature, StringComparison.Ordinal))
            {
                _shownStageKey = stageSignature;
                _shownStage = 0f;
            }

            double stageStall = EditorApplication.timeSinceStartup - _stageChangedAt;
            int stage = 0;
            if (!string.IsNullOrEmpty(state.StandStage))
            {
                stage = GenerationProgress.StagePercent(state.StandStage,
                    state.StandStageDone, state.StandStageTotal, stageStall);
            }
            else if (current != null && !string.IsNullOrEmpty(current.Stage))
            {
                stage = GenerationProgress.StagePercent(current.Stage, 0, 0,
                    stageStall);
            }

            string stageCaptionText = GenerationProgress.StandStageText(
                state.StandStage, state.StandStageDone, state.StandStageTotal,
                state.StandStageText);
            if (string.IsNullOrEmpty(stageCaptionText) && current != null
                && !string.IsNullOrEmpty(current.Stage))
            {
                stageCaptionText = GenerationProgress.StageTitle(current.Stage);
            }

            if (_generationRunning)
            {
                int index = 0;
                for (int i = 0; i < jobs.Jobs.Count; i++)
                {
                    if (ReferenceEquals(jobs.Jobs[i], current))
                    {
                        index = i + 1;
                        break;
                    }
                }

                int ok = jobs.Finished - jobs.Failed;

                _overallTarget = overall;
                _generationTitle.text = $"Генерация · кадр {index} из {jobs.Total}";
                SetText(_overallCaption, $"Вся заявка · {overall}%");
                _generationStatus.text = jobs.Failed > 0
                    ? $"готово {ok} из {jobs.Total} · сбоев {jobs.Failed}"
                    : $"готово {ok} из {jobs.Total}";

                _frameTarget = frame;
                SetText(_frameCaption,
                    $"Текущий кадр · {GenerationProgress.JobLabel(current)} · {frame}%");
                _stageTarget = stage;
                SetText(_stageCaption, stageCaptionText);
                _generationPhase.text = GenerationProgress.PhaseText(current, stallSeconds);
                _generationEta.text = GenerationProgress.EtaText(jobs, current);
            }
            else if (show)
            {
                int ok = jobs.Finished - jobs.Failed;
                _overallTarget = 100;
                _frameTarget = 100;
                _stageTarget = 100;
                _generationTitle.text = "Генерация завершена";
                SetText(_overallCaption, "Вся заявка · 100%");
                SetText(_frameCaption, "Текущий кадр · готов");
                SetText(_stageCaption, "Все этапы пройдены");
                _generationStatus.text = jobs.Failed > 0
                    ? $"готово {ok} из {jobs.Total}, сбоев {jobs.Failed}"
                    : $"готово {ok} из {jobs.Total} — кадры ждут в «На ревью»";
                _generationPhase.text = string.Empty;
                _generationEta.text = string.Empty;
            }
            else
            {
                _overallTarget = 0;
                _frameTarget = 0;
                _stageTarget = 0;
                _generationTitle.text = "Генерация не запущена";
                SetText(_overallCaption, "Вся заявка");
                SetText(_frameCaption, "Текущий кадр");
                SetText(_stageCaption, "Текущий этап");
                _generationStatus.text =
                    "выбери пул в дереве, укажи количество и нажми «Сгенерировать»";
                _generationPhase.text = string.Empty;
                _generationEta.text = string.Empty;
            }

            // Прогон начался — открываем вкладку генератора сама. Иначе полоса
            // живёт на неактивной вкладке, и второе нажатие «Сгенерировать»
            // выглядит как «ничего не произошло».
            if (justStarted)
            {
                SetRightPage(RightPage.Generator);
            }

            // Прогресс в заголовке вкладки виден, даже когда она закрыта:
            // раньше это показывала подпись кнопки в тулбаре, которой больше нет.
            _tabGenerator.text = _generationRunning && _rightPage != RightPage.Generator
                ? $"Генератор · {overall}%"
                : "Генератор";
        }


        /// <summary>
        /// Страница «Генератор»: точки состояния, сводка по стенду и строка
        /// «что происходит сейчас».
        ///
        /// ПОЧЕМУ ВСЁ В ОДНОМ АДРЕСЕ. Раньше состояние было размазано: точки и
        /// кнопки — в верхней строке, сводка — во второй её строке, прогресс —
        /// в центре. Одно и то же приходилось читать в двух местах, и они
        /// расходились. Теперь у состояния ровно один адрес.
        /// </summary>
        private void UpdateStatusBar(ArtDriverState state)
        {
            SetDot(_driverDot, state.ProtocolMismatch
                ? "is-busy"
                : state.DriverConnected ? "is-on" : "is-off");

            string server = state.ServerState ?? "—";
            SetDot(_serverDot,
                server.Contains("готов") ? "is-on"
                : server.Contains("поднима") ? "is-busy"
                : "is-off");

            SetText(_infoDriver, state.DriverConnected
                ? $"драйвер: подключён · протокол v{state.DriverProtocol}"
                : "драйвер: не подключён");
            SetText(_infoServer, $"сервер: {server}");
            RenderStandInfo(state.StandInfoLines);
            SetText(_infoStyle, _settings == null
                ? "стиль: —"
                : $"стиль: {_settings.Style} · свет: {(_settings.Light ?? "по кадру")}");
            // Счётчики стоят в секции «Что можно сгенерировать», а подписи называют
            // смысл. «Пулов на стенде: 81» отвечало на вопрос, которого никто
            // не задавал; полезны две другие вещи — сколько групп можно выбрать
            // и сколько кадров добавлю ВСЕГО ОДИН.
            string selectedPrefix = SelectedPoolPrefix();
            DriverPool selectedPool = state.FindPool(selectedPrefix);

            SetText(_infoPools, $"групп кадров на стенде: {state.Pools.Count}");
            _infoPools.tooltip =
                "Пул — это именованная группа кадров: City_GuildClerk, "
                + "Craftsmen_Potter, Roles_Priest. Из них выбираешь, что рисовать.";

            SetText(_infoFree, $"свободно во всех группах: {state.FreeFrames}");
            _infoFree.tooltip =
                "Сумма незаполненных слотов по всем группам сразу. Сколько "
                + "кадров осталось нарисовать ВООБЩЕ, а не в выбранной.";

            SetText(_infoPoolFree, selectedPool == null
                ? "свободно в выбранном пуле: —"
                : $"свободно в пуле {selectedPool.Prefix}: {selectedPool.Free.Count}");
            _infoPoolFree.tooltip =
                "Это то, что нужно при выборе пула: сколько кадров можно "
                + "добавить прямо сейчас.";

            JobQueue jobs = state.Jobs;
            SetText(_infoQueue, _generationRunning
                ? $"очередь: идёт {jobs.Finished}/{jobs.Total}"
                : jobs.Total > 0
                    ? $"очередь: {jobs.Finished}/{jobs.Total}"
                    : "очередь: пусто");

            // Разделяем смысл и шум. «Что происходит» показывает только
            // ошибку — это то, на что человек обязан отреагировать. Поток
            // строк `shoot.py` (сотни технических сообщений за прогон) уходит
            // в приглушённую строку «последняя диагностика»: раньше он шёл
            // сюда же и превращал раздел в бегущую консоль.
            SetText(_infoActivity, !string.IsNullOrEmpty(state.LastError)
                ? state.LastError
                : "—");
            SetText(_generationLog, !string.IsNullOrEmpty(state.LastLog)
                ? "последняя диагностика: " + state.LastLog
                : string.Empty);
        }

        /// <summary>
        /// Ставит текст, только если он изменился.
        ///
        /// ЗАЧЕМ. Опрос идёт раз в 150 мс, а подписей на странице десяток.
        /// Присваивание одного и того же текста каждый раз помечает элемент
        /// изменённым и гонит раскладку всего окна — на прогоне это видно как
        /// подёргивание интерфейса редактора.
        /// </summary>
        private static void SetText(TextElement label, string text)
        {
            string value = text ?? string.Empty;
            if (label != null && label.text != value)
            {
                label.text = value;
            }
        }

        private static void SetDot(VisualElement dot, string stateClass)
        {
            if (dot == null)
            {
                return;
            }

            dot.EnableInClassList("is-on", stateClass == "is-on");
            dot.EnableInClassList("is-busy", stateClass == "is-busy");
            dot.EnableInClassList("is-off", stateClass == "is-off");
        }

        /// <summary>
        /// Сколько кадров пула можно сделать, НЕ трогая файл доп. кадров:
        /// свободные id минус те, что уже лежат кандидатами.
        ///
        /// Почему с вычетом. Драйвер считает свободным всё, чего нет в
        /// проекте, — а сгенерированный кандидат в проект ещё не попал. Без
        /// вычета повторное «добавить» выбрало бы те же id и перегенерировало
        /// уже сделанных кандидатов вместо новых вариантов.
        /// </summary>
        private int AvailableIds(DriverPool pool)
        {
            if (pool == null)
            {
                return 0;
            }

            int pending = pool.Free.Count(id => _reviewCandidates.Any(candidate =>
                string.Equals(candidate.StandId, id, StringComparison.Ordinal)));

            return Mathf.Max(0, pool.Free.Count - pending);
        }

        /// <summary>
        /// Буквы, уже занятые в пуле: ростер (из каталога) плюс файл доп.
        /// кадров. По ним видно, что буква занята, — иначе повтор молча
        /// превратился бы в отказ стенда.
        /// </summary>
        private List<string> UsedLetters(string prefix)
        {
            var used = new List<string>();
            if (string.IsNullOrEmpty(prefix))
            {
                return used;
            }

            DriverPool pool = _client?.State.FindPool(prefix);
            if (pool != null)
            {
                foreach (string id in pool.All)
                {
                    string letter = ExtraPoolsFile.LetterOf(id);
                    if (letter.Length > 0 && !used.Contains(letter))
                    {
                        used.Add(letter);
                    }
                }
            }

            ExtraPoolsFile.Entry entry = _extraPools?.Find(prefix);
            if (entry != null)
            {
                foreach (char letter in entry.Letters)
                {
                    string value = letter.ToString();
                    if (!used.Contains(value))
                    {
                        used.Add(value);
                    }
                }
            }

            return used;
        }

        // -------------------------------------------------------------- доп. места

        /// <summary>
        /// Список новых мест фона. У фона нет буквы-кадра (номер всегда _01),
        /// поэтому «добавить кадр» фону — это добавить место.
        /// </summary>
        private void RenderExtraPlaces()
        {
            if (_placeList == null || _extraPools == null)
            {
                return;
            }

            // Подключение входит в подпись: кнопка «Сгенерировать» у места
            // оживает только с драйвером, а список иначе не перерисовался бы.
            bool connected = _client != null && _client.State.DriverConnected;
            string signature = connected + "|" + string.Join("|", _extraPools.Places
                .Select(place => $"{place.StandId}:{place.Label}"));
            if (signature == _placeSignature)
            {
                return;
            }

            _placeSignature = signature;
            _placeList.Clear();

            if (_placesToggle != null)
            {
                _placesToggle.text = _extraPools.PlaceCount > 0
                    ? $"Доп. места (фоны) ({_extraPools.PlaceCount})"
                    : "Доп. места (фоны)";
            }

            if (_extraPools.PlaceCount == 0)
            {
                var empty = new Label("нет доп. мест");
                empty.AddToClassList("extra-empty");
                _placeList.Add(empty);
                return;
            }

            foreach (ExtraPoolsFile.Place place in _extraPools.Places)
            {
                var row = new VisualElement();
                row.AddToClassList("extra-row");

                var name = new Label($"{place.StandId} — {place.Label}");
                name.AddToClassList("extra-row-name");

                // Сгенерировать место нужно прямо здесь: у нового места ещё
                // нет кадров в проекте, поэтому в дереве его нет, выбрать его
                // там нечем — и без этой кнопки добавить фон было бы нечем
                // воспользоваться. Пула нет в дереве, пока кадр не одобрен.
                var generate = new Button(() => GeneratePlace(place.StandId))
                {
                    text = "Сгенерировать"
                };
                generate.AddToClassList("extra-row-generate");
                generate.SetEnabled(connected);

                var remove = new Button(() => RemoveExtraPlace(place.StandId)) { text = "×" };
                remove.AddToClassList("extra-row-remove");

                row.Add(name);
                row.Add(generate);
                row.Add(remove);
                _placeList.Add(row);
            }
        }

        private void GeneratePlace(string standId)
        {
            _client?.Generate(standId, 1);
        }

        /// <summary>
        /// Добавляет новое место фона. Сцену окно не сочиняет — это промпт
        /// стенда, человек его приносит; окно только переносит и проверяет,
        /// что место попало в известную группу и не занято.
        /// </summary>
        private void AddExtraPlace()
        {
            string group = _placeGroup?.value;
            string place = _placeId?.value;
            string label = _placeLabel?.value;
            string scene = _placeScene?.value;

            List<string> problems = ExtraPoolsFile.ValidatePlace(
                group, place, label, scene,
                StandGroupMapper.BackgroundStandGroups, UsedPlaceIds());

            if (problems.Count > 0)
            {
                _extraProblems.Clear();
                _extraProblems.AddRange(problems);
                WriteStatus(_loadProblems);
                return;
            }

            if (!_extraPools.AddPlace(group.Trim(), place.Trim(), label.Trim(), scene.Trim()))
            {
                _extraProblems.Clear();
                _extraProblems.Add($"доп. места: bg_{group}_{place} уже есть");
                WriteStatus(_loadProblems);
                return;
            }

            _extraPools.Save(ArtStudioPaths.ExtraPools(_projectRoot));
            _placeId.SetValueWithoutNotify(string.Empty);
            _placeLabel.SetValueWithoutNotify(string.Empty);
            _placeScene.SetValueWithoutNotify(string.Empty);
            _extraProblems.Clear();
            _placeSignature = null;

            // Новое место — новый пул фона: без запроса каталога его не будет
            // ни в дереве, ни в списке свободных кадров.
            _client?.RequestCatalog();
            RenderExtraPlaces();
            WriteStatus(_loadProblems);
        }

        private void RemoveExtraPlace(string standId)
        {
            if (_extraPools == null || !_extraPools.RemovePlace(standId))
            {
                return;
            }

            _extraPools.Save(ArtStudioPaths.ExtraPools(_projectRoot));
            _placeSignature = null;
            _extraProblems.Clear();
            _client?.RequestCatalog();
            RenderExtraPlaces();
            WriteStatus(_loadProblems);
        }

        /// <summary>
        /// Занятые id фонов: ростер из каталога плюс файл. По ним видно, что
        /// место уже есть, — иначе повтор молча превратился бы в отказ стенда.
        /// </summary>
        private List<string> UsedPlaceIds()
        {
            var used = new List<string>();

            if (_client != null)
            {
                foreach (DriverPool pool in _client.State.Pools.Values)
                {
                    if (pool.Kind != ArtAssetKind.Background)
                    {
                        continue;
                    }

                    foreach (string id in pool.All)
                    {
                        if (!used.Contains(id))
                        {
                            used.Add(id);
                        }
                    }
                }
            }

            if (_extraPools != null)
            {
                foreach (ExtraPoolsFile.Place place in _extraPools.Places)
                {
                    if (!used.Contains(place.StandId))
                    {
                        used.Add(place.StandId);
                    }
                }
            }

            return used;
        }

        // ------------------------------------------------------------------ ревью

        /// <summary>
        /// Перечитывает папку ревью. Заодно снимает пометки брака, которые
        /// устарели: если файл кандидата перезаписан ПОСЛЕ пометки, значит
        /// кадр уже перегенерирован, и старая пометка не должна тянуть счётчик
        /// «Перегенерировать брак» дальше — иначе человек гонял бы один и тот
        /// же кадр бесконечно. Раньше это ловилось событием драйвера, а
        /// событие теряется вместе с перезапуском.
        /// </summary>
        private void RescanReview()
        {
            ReviewScanner.ScanResult scan = ReviewScanner.Scan(ArtStudioPaths.Review(_projectRoot));

            bool cleared = false;
            foreach (ReviewCandidate candidate in scan.Candidates)
            {
                cleared |= ClearStaleMark(candidate);
            }

            // Кандидат исчез — принят или удалён; пометка по его пути тоже
            // ни на что не влияет и только портит счётчик.
            foreach (string key in _feedback.Keys.ToList())
            {
                if (!IsReviewPath(key))
                {
                    continue;
                }

                bool alive = scan.Candidates.Any(candidate =>
                    string.Equals(candidate.Path, key, StringComparison.Ordinal));
                if (!alive)
                {
                    cleared |= _feedback.Remove(key);
                }
            }

            _reviewCandidates.Clear();
            _reviewCandidates.AddRange(scan.Candidates);

            _reviewProblems.Clear();
            _reviewProblems.AddRange(scan.Problems);

            if (cleared)
            {
                Persist();
            }

            if (_selectedCandidate != null)
            {
                string path = _selectedCandidate.Path;
                _selectedCandidate = _reviewCandidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Path, path, StringComparison.Ordinal));
            }

            // Состав очереди входит в подпись левой панели, поэтому её надо
            // пересобрать — и счётчики «на ревью N» у пулов тоже.
            _leftSignature = null;
            _reviewSignature = null;
        }

        private bool ClearStaleMark(ReviewCandidate candidate)
        {
            FeedbackStore.Entry entry = _feedback.Get(candidate.Path);
            if (entry == null || entry.Status != ArtStatus.Rejected)
            {
                return false;
            }

            if (!DateTime.TryParse(entry.At, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime marked))
            {
                return false;
            }

            DateTime written = new DateTime(candidate.WrittenUtcTicks, DateTimeKind.Utc).ToLocalTime();
            return written > marked && _feedback.Remove(candidate.Path);
        }

        /// <summary>Путь лежит в папке ревью (а не в арте проекта).</summary>
        private bool IsReviewPath(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            string review = Path.GetFullPath(ArtStudioPaths.Review(_projectRoot)).Replace('\\', '/');
            string path = Path.GetFullPath(key).Replace('\\', '/');
            return path.StartsWith(review + "/", StringComparison.OrdinalIgnoreCase);
        }

        private void SelectCandidate(ReviewCandidate candidate)
        {
            _selectedCandidate = candidate;
            _selected = null;

            // Список кадров снимает выделение: панель показывает один объект,
            // и два подсвеченных выбора читались бы как два решения.
            _frames.ClearSelection();

            RenderLeftPanel();

            // Выбор кандидата тоже не переключает вкладку — по той же
            // причине, что и выбор кадра: человек вправе остаться там, где смотрит.
            UpdateDetailPanel();
        }

        /// <summary>
        /// Кандидат лежит вне <c>Assets</c>, поэтому <see cref="AssetDatabase"/> его
        /// не знает: читаем PNG с диска. Кэш живёт до закрытия окна — иначе
        /// список кандидатов читал бы файл каждые 150 мс.
        /// </summary>
        private Texture2D CandidateTexture(string path)
        {
            if (_candidateTextures.TryGetValue(path, out Texture2D cached))
            {
                // Текстура могла не пережить перезагрузку домена: запись в
                // кэше осталась, а Unity-объект уже мёртв. Сравнение с null у
                // UnityEngine.Object ловит и уничтоженный объект — без этой
                // проверки миниатюра оставалась пустой навсегда.
                if (cached != null)
                {
                    return cached;
                }

                _candidateTextures.Remove(path);
            }

            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path)))
                {
                    DestroyImmediate(texture);
                    return null;
                }
            }
            catch (Exception)
            {
                DestroyImmediate(texture);
                return null;
            }

            _candidateTextures[path] = texture;
            return texture;
        }

        private void OnDisable()
        {
            _client?.Dispose();

            foreach (Texture2D texture in _candidateTextures.Values)
            {
                if (texture != null)
                {
                    DestroyImmediate(texture);
                }
            }

            _candidateTextures.Clear();
        }

        private static string StatusText(ArtStatus status)
        {
            switch (status)
            {
                case ArtStatus.Approved: return "принят";
                case ArtStatus.Rejected: return "брак";
                default: return "не смотрели";
            }
        }

        private static string StatusClass(ArtStatus status)
        {
            switch (status)
            {
                case ArtStatus.Approved: return "badge-approved";
                case ArtStatus.Rejected: return "badge-rejected";
                default: return "badge-new";
            }
        }
    }
}
