using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace TcpJsonClient
{
    

    public partial class MainForm : Form
    {
        private TcpClient _client;
        private TcpListener _server;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private JavaScriptSerializer _serializer = new JavaScriptSerializer();

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
                Disconnect();
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
                    await StartServer(ip, port);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("發生錯誤: " + ex.Message);
                Disconnect();
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
            Task.Run(() => ReceiveLoop(_cts.Token));
        }

        private async Task StartServer(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Parse(ip);
            _server = new TcpListener(localAddr, port);
            _server.Start();
            UpdateStatus("等待連入...");
            btnConnect.Text = "停止";

            try
            {
                _client = await _server.AcceptTcpClientAsync();
                _stream = _client.GetStream();
                UpdateStatus("Client 已連入");
                Task.Run(() => ReceiveLoop(_cts.Token));
            }
            catch (Exception)
            {
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested && _client != null && _client.Connected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (bytesRead == 0) break;

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    try
                    {
                        var payload = _serializer.Deserialize<MessagePayload>(json);
                        string displayMsg = string.Format("[{0}] {1}: {2}", 
                            payload.Timestamp, payload.Sender, payload.Content);
                        AppendReceiveText(displayMsg);
                    }
                    catch (Exception)
                    {
                        AppendReceiveText("收到格式錯誤的資料: " + json);
                    }
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                this.Invoke(new Action(() => Disconnect()));
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (_client == null || !_client.Connected)
            {
                MessageBox.Show("尚未建立通訊");
                return;
            }

            try
            {
                var payload = new MessagePayload
                {
                    Sender = txtName.Text,
                    Content = txtSend.Text,
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Mode = cmbMode.SelectedItem.ToString()
                };

                string json = _serializer.Serialize(payload);
                byte[] data = Encoding.UTF8.GetBytes(json);
                _stream.Write(data, 0, data.Length);
                
                // 清空發送框
                txtSend.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show("發送失敗: " + ex.Message);
            }
        }

        private void Disconnect()
        {
            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
            _server?.Stop();
            
            _client = null;
            _stream = null;
            _server = null;
            _cts = null;

            UpdateStatus("已斷線");
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
            txtReceive.AppendText(text + Environment.NewLine);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Disconnect();
        }
    }

    public class MessagePayload
    {
        public string Sender { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
        public string Mode { get; set; }
    }
}
