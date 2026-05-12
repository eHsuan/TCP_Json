using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpJsonClient
{
    public partial class MainForm : Form
    {
        private TcpClient _client;
        private TcpListener _server;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            cmbMode.SelectedIndex = 0; // 預設為 Client
        }

        private void cmbMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbMode.SelectedItem.ToString() == "Server")
            {
                txtIP.Text = "0.0.0.0";
                btnConnect.Text = "啟動";
                txtName.Text = "Server";
            }
            else
            {
                txtIP.Text = "127.0.0.1";
                btnConnect.Text = "連線";
                txtName.Text = "Client";
            }
        }

        private async void btnConnect_Click(object sender, EventArgs e)
        {
            if (btnConnect.Text == "中斷" || btnConnect.Text == "停止")
            {
                StopAll();
                return;
            }

            try
            {
                string ip = txtIP.Text;
                int port = int.Parse(txtPort.Text);
                _cts = new CancellationTokenSource();

                if (cmbMode.SelectedItem.ToString() == "Client")
                {
                    await StartClient(ip, port);
                }
                else
                {
                    // Server 模式啟動後不 await，因為它會進入無限監聽迴圈
                    _ = StartServer(ip, port, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("發生錯誤: " + ex.Message);
                StopAll();
            }
        }

        private async Task StartClient(string ip, int port)
        {
            _client = new TcpClient();
            UpdateStatus("連線中...");
            await _client.ConnectAsync(ip, port);
            _stream = _client.GetStream();
            UpdateStatus("已連線");
            btnConnect.Text = "中斷";
            _ = ReceiveLoop(_client, _cts.Token);
        }

        private async Task StartServer(string ip, int port, CancellationToken token)
        {
            try
            {
                IPAddress localAddr = IPAddress.Parse(ip);
                _server = new TcpListener(localAddr, port);
                _server.Start();
                UpdateStatus("Server 已啟動，監聽中...");
                btnConnect.Text = "停止";

                while (!token.IsCancellationRequested)
                {
                    // 等待新的 Client 連入
                    TcpClient client = await _server.AcceptTcpClientAsync();
                    AppendReceiveText($"[系統] 新的 Client 已連入: {client.Client.RemoteEndPoint}");
                    
                    // 針對每個連入的 Client 啟動獨立的接收迴圈
                    _ = ReceiveLoop(client, token);
                }
            }
            catch (ObjectDisposedException)
            {
                // Server 被停止時會觸發此異常，正常現象
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    AppendReceiveText($"[系統錯誤] Server 異常: {ex.Message}");
            }
            finally
            {
                UpdateStatus("Server 已停止");
            }
        }

        private async Task ReceiveLoop(TcpClient client, CancellationToken token)
        {
            // 移植來源專案使用的 1MB 緩衝區
            byte[] buffer = new byte[1024 * 1024];
            NetworkStream stream = client.GetStream();

            try
            {
                while (!token.IsCancellationRequested && client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0) break;

                    string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    AppendReceiveText($"[接收] {msg}");

                    // 移植 ZBTAOIFunction 的 Link1 回覆邏輯
                    if (msg.Contains("Link1"))
                    {
                        await SendRawMessage(stream, client, "Ack");
                        AppendReceiveText("[自動回覆] Ack");
                    }

                    // 如果是 Server 模式，我們可以更新當前的 _client 與 _stream 方便手動發送給最後一個連入的人
                    if (cmbMode.SelectedItem.ToString() == "Server")
                    {
                        _client = client;
                        _stream = stream;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略斷線異常
            }
            finally
            {
                AppendReceiveText($"[系統] Client 已中斷連線");
                stream.Close();
                client.Close();
                
                if (cmbMode.SelectedItem.ToString() == "Client")
                {
                    this.Invoke(new Action(() => StopAll()));
                }
            }
        }

        private async void btnSend_Click(object sender, EventArgs e)
        {
            if (_client == null || !_client.Connected)
            {
                MessageBox.Show("尚未建立通訊或 Client 已斷開");
                return;
            }

            string textToSend = txtSend.Text;
            if (string.IsNullOrEmpty(textToSend)) return;

            try
            {
                await SendRawMessage(_stream, _client, textToSend);
                AppendReceiveText($"[發送] {textToSend}");
                txtSend.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("發送失敗: " + ex.Message);
            }
        }

        private async Task SendRawMessage(NetworkStream stream, TcpClient client, string msg)
        {
            if (stream != null && client != null && client.Connected)
            {
                byte[] data = Encoding.UTF8.GetBytes(msg);
                await stream.WriteAsync(data, 0, data.Length);
            }
        }

        private void StopAll()
        {
            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
            _server?.Stop();
            
            _client = null;
            _stream = null;
            _server = null;
            _cts = null;

            UpdateStatus("已停止");
            btnConnect.Text = cmbMode.SelectedItem.ToString() == "Server" ? "啟動" : "連線";
        }

        private void UpdateStatus(string status)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(UpdateStatus), status);
                return;
            }
            lblStatus.Text = status;
        }

        private void AppendReceiveText(string text)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(AppendReceiveText), text);
                return;
            }
            txtReceive.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopAll();
        }
    }
}
