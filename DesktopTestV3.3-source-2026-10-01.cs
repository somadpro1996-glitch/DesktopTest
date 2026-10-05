using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("桌面测试工具")]
[assembly: System.Reflection.AssemblyDescription("桌面交互测试工具")]
[assembly: System.Reflection.AssemblyProduct("Desktop Test Utility")]
[assembly: System.Reflection.AssemblyCompany("")]
[assembly: System.Reflection.AssemblyVersion("1.2.3.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.3.0")]
namespace DesktopTest {
    static class Profile {
        internal static int Horizontal(long cycle) { return (cycle % 2 == 0) ? 2 : 3; }
        internal static int FirstY(long elapsed) { return 6; }
        internal static int SecondY(long elapsed) { return -1; }
    }
    // Requires right-then-left activation; releasing either button cancels.
    public sealed class Trigger {
        private bool previousLeft;
        public bool Active { get; private set; }
        public bool Update(bool enabled, bool left, bool right) {
            if (!enabled || !left || !right) Active = false;
            else if (!previousLeft && right) Active = true;
            previousLeft = left;
            return Active;
        }
    }
    static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct PointerInput {
            public int dx, dy;
            public uint data, flags, time;
            public UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Input {
            public uint type;
            public PointerInput pointer;
        }
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
        internal static bool Down(int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; }
        internal static bool Move(int x, int y) {
            Input[] inputs = { new Input { type = 0, pointer = new PointerInput { dx = x, dy = y, flags = 1 } } };
            return SendInput(1, inputs, Marshal.SizeOf(typeof(Input))) == 1;
        }
    }
    sealed class MainForm : Form {
        volatile bool running, enabled, closing;
        const int delay = 1;
        volatile string problem = "";
        long cycles;
        long downward;
        readonly Label state = new Label();
        readonly Label stats = new Label();
        readonly Button start = new Button();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        readonly Thread worker;

        public MainForm() {
            Text = "桌面测试工具";
            ClientSize = new Size(650, 470);
            Font = new Font("Microsoft YaHei UI", 10F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            Label title = new Label { Text = "桌面测试工具", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), Location = new Point(26, 20), AutoSize = true };
            Controls.Add(title);
            Controls.Add(new Label { Text = "先点击“开始测试”，再按住右键，最后按住左键。\n光标会快速往返抖动，并持续向下移动。", Location = new Point(28, 75), Size = new Size(592, 58) });
            Controls.Add(new Label { Text = "每步等待 1 毫秒；全程每轮向下 5 单位，左右幅度交替 2 / 3（平均 2.5）。", Location = new Point(28, 145), AutoSize = true });
            start.Text = "开始测试"; start.Location = new Point(28, 190); start.Size = new Size(145, 42);
            start.Click += delegate {
                if (running) Stop();
                else {
                    problem = ""; Interlocked.Exchange(ref cycles, 0); Interlocked.Exchange(ref downward, 0);
                    enabled = true; running = true;
                }
                RefreshStatus();
            };
            Controls.Add(start);
            state.Location = new Point(193, 199); state.Size = new Size(420, 32); Controls.Add(state);
            stats.Location = new Point(28, 245); stats.Size = new Size(580, 30); Controls.Add(stats);
            Panel testArea = new Panel { Location = new Point(28, 286), Size = new Size(592, 75), BackColor = Color.FromArgb(226, 234, 243), BorderStyle = BorderStyle.FixedSingle };
            testArea.Controls.Add(new Label { Text = "可在这块空白区域按右键 + 左键测试\n左右幅度按轮交替 2 / 3；每轮下 6、上 1，净下拉 5", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
            Controls.Add(testArea);
            Controls.Add(new Label { Text = "侧键 5：暂停 / 恢复　｜　窗口按钮：结束测试\n松开左键或右键立即停止移动。\n程序只移动光标，不会自动点击光标。", Location = new Point(28, 379), Size = new Size(596, 76) });
            refresh.Interval = 100; refresh.Tick += delegate { RefreshStatus(); }; refresh.Start();
            worker = new Thread(Work); worker.IsBackground = true; worker.Start();
            FormClosing += delegate { closing = true; Stop(); refresh.Stop(); worker.Join(250); };
            RefreshStatus();
        }
        void Stop() { running = false; enabled = false; }
        void RefreshStatus() {
            start.Text = running ? "结束测试" : "开始测试";
            state.Text = problem.Length > 0 ? problem : !running ? "已停止 · 不移动光标" : enabled ? "已就绪 · 右键 + 左键触发" : "已暂停 · 5 号键恢复";
            long count = Interlocked.Read(ref cycles);
            stats.Text = "已完成 " + count + " 轮；累计向下输入 " + Interlocked.Read(ref downward) + " 单位";
        }
        bool CanMove() { return running && enabled && !closing && Native.Down(0x01) && Native.Down(0x02); }
        void Work() {
            Trigger trigger = new Trigger();
            Stopwatch burst = new Stopwatch();
            long burstCycles = 0;
            bool previousSide = Native.Down(0x06);
            while (!closing) {
                bool side = Native.Down(0x06);
                if (side && !previousSide && running) enabled = !enabled;
                previousSide = side;
                bool left = Native.Down(0x01), right = Native.Down(0x02);
                bool wasActive = trigger.Active;
                if (trigger.Update(CanMove(), left, right)) {
                    if (!wasActive) { burst.Restart(); burstCycles = 0; }
                    int horizontal = Profile.Horizontal(burstCycles++);
                    long elapsed = burst.ElapsedMilliseconds;
                    int firstY = Profile.FirstY(elapsed), secondY = Profile.SecondY(elapsed);
                    if (!CanMove()) { trigger.Update(false, left, right); continue; }
                    if (!Native.Move(-horizontal, firstY)) { problem = "系统未接受光标输入，测试已停止"; Stop(); continue; }
                    Interlocked.Add(ref downward, firstY);
                    Thread.Sleep(delay);
                    if (!CanMove()) { trigger.Update(false, Native.Down(0x01), Native.Down(0x02)); continue; }
                    if (!Native.Move(horizontal, secondY)) { problem = "系统未接受光标输入，测试已停止"; Stop(); continue; }
                    Interlocked.Add(ref downward, secondY);
                    Interlocked.Increment(ref cycles);
                    Thread.Sleep(delay);
                } else Thread.Sleep(2);
            }
        }
    }
    static class Program {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void SelfTest() {
            Check(Profile.Horizontal(0) == 2 && Profile.Horizontal(1) == 3, "Alternating displacement");
            Check(Profile.Horizontal(2) == 2 && Profile.Horizontal(3) == 3, "Alternation repeats");
            Check((Profile.Horizontal(0) + Profile.Horizontal(1)) / 2.0 == 2.5, "Mean horizontal amplitude");
            Check(Profile.FirstY(0) + Profile.SecondY(0) == 5, "Initial displacement");
            Check(Profile.FirstY(299) + Profile.SecondY(299) == 5, "Initial phase end");
            Check(Profile.FirstY(300) + Profile.SecondY(300) == 5, "Phase transition");
            Check(Profile.FirstY(10000) + Profile.SecondY(10000) == 5, "Sustained displacement");
            Trigger t = new Trigger();
            Check(!t.Update(true, false, true), "Right alone");
            Check(t.Update(true, true, true), "Right then left");
            Check(!t.Update(true, true, false), "Right release stops");
            Check(!t.Update(true, true, true), "Right repress does not reactivate held left");
            Check(!t.Update(true, false, false), "Left release stops");
            Check(!t.Update(true, true, false), "Left alone");
            Check(!t.Update(true, true, true), "Left then right does not trigger");
            t.Update(true, false, true);
            Check(t.Update(true, true, true), "Retrigger");
            Check(!t.Update(false, true, true), "Emergency disable");
            Check(!t.Update(true, true, true), "No retrigger while left held");
            Check(Marshal.SizeOf(typeof(Native.Input)) == (IntPtr.Size == 8 ? 40 : 28), "Native INPUT layout");
        }
        [STAThread] static int Main(string[] args) {
            if (args.Length > 0 && args[0] == "--self-test") {
                try { SelfTest(); return 0; } catch { return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}







