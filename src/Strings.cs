namespace VanishFF
{
    // Словарь всех строк интерфейса (ТЗ 1.4). Английский язык (фаза 3)
    // добавится вторым набором значений с переключателем — поэтому все
    // тексты живут только здесь, в коде форм строковых литералов нет.
    static class L
    {
        public const string AppTitle = "Vanish-FFConverter";

        public const string ErrNoFFmpeg =
            "Не найден ffmpeg\\ffmpeg.exe рядом с программой.\n\n" +
            "Положите сборку ffmpeg (ffmpeg.exe, ffprobe.exe, ffplay.exe)\n" +
            "в папку ffmpeg и запустите программу снова.";

        public const string TabAudio = "Аудио";
        public const string TabVideo = "Видео";
        public const string TabRemux = "Без перекодирования";
        public const string TabCut = "Резка и склейка";
        public const string TabInspect = "Инспекция";
        public const string TabTools = "Инструменты";

        public const string StatusIdle = "Готов";
        public const string StubPhase = "Вкладка «{0}» — в разработке ({1})";
        public const string StubPhase1 = "фаза 1, следующая на очереди";
        public const string StubPhase2 = "фаза 2";
        public const string StubPhase3 = "фаза 3";

        public const string ThemeToggleTip = "Тема: тёмная / светлая";
    }
}
