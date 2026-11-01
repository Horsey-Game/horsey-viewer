namespace horsey
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            string? game = GameFolder.Find() ?? AskForGameFolder();
            if (game is null) return;

            GameFolder.Root = game;
            Application.Run(new Form1());
        }

        // Asks for the game folder until the user picks one with Horsey.exe in it, or cancels.
        private static string? AskForGameFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Pick your Horsey Game folder (the one with Horsey.exe in it).",
                UseDescriptionForTitle = true,
            };
            while (dialog.ShowDialog() == DialogResult.OK)
            {
                if (GameFolder.IsGameFolder(dialog.SelectedPath))
                {
                    GameFolder.Remember(dialog.SelectedPath);
                    return dialog.SelectedPath;
                }
                MessageBox.Show("Horsey.exe is not in that folder. Try again.", "Horsey viewer");
            }
            return null;
        }
    }
}
