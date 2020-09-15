using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows;

namespace TCP_Client
{
	/// <summary>
	/// TCP非同期送受信Class
	/// </summary>
	internal class TCPClient : IDisposable
	{
		/// <summary>
		/// 各イベントのタイムアウト
		/// </summary>
		private const int timeOut = 5000;

		/// <summary>
		/// サーバのIPアドレス
		/// </summary>
		private readonly IPAddress address;

		/// <summary>
		/// サーバのポート番号
		/// </summary>
		private readonly int port;

		/// <summary>
		/// エラー発生時のメッセージ
		/// </summary>
		private readonly bool isMessage;

		/// <summary>
		/// 接続完了時にセットされるイベント
		/// </summary>
		private ManualResetEventSlim connectDone = new ManualResetEventSlim(false);

		/// <summary>
		/// 受信完了時にセットされるイベント
		/// </summary>
		private ManualResetEventSlim sendDone = new ManualResetEventSlim(false);

		/// <summary>
		/// 送信完了時にセットされるイベント
		/// </summary>
		private ManualResetEventSlim receiveDone = new ManualResetEventSlim(false);

		/// <summary>
		/// ログ出力用デリゲートのメソッド型
		/// </summary>
		/// <param name="message"></param>
		internal delegate void LogDelegate(string message);

		/// <summary>
		/// ログ出力デリゲート
		/// </summary>
		internal LogDelegate logDelegate = null;

		/// <summary>
		/// コンストラクタ
		/// </summary>
		/// <param name="Address">接続するサーバのIPアドレスの文字列</param>
		/// <param name="Port">接続するサーバのポート番号</param>
		/// <param name="Log">ログ出力用デリゲート</param>
		/// <param name="IsMessage">エラーメッセージ false=表示しない true=表示する</param>
		internal TCPClient(string Address, int Port, LogDelegate Log = null, bool IsMessage = false)
		{
			address = IPAddress.Parse(Address);
			port = Port;
			isMessage = IsMessage;
			// ログを記録する設定ならLog Classのインスタンスを生成
			if (Log != null)
			{
				logDelegate = Log;
			}
		}

		/// <summary>
		/// デストラクタ
		/// </summary>
		~TCPClient()
		{
			// イベントの事後処理
			connectDone?.Dispose();
			connectDone = null;
			sendDone?.Dispose();
			sendDone = null;
			receiveDone?.Dispose();
			receiveDone = null;
		}

		/// <summary>
		/// 終了時の処理
		/// </summary>
		public void Dispose()
		{
			// イベントの事後処理
			connectDone?.Dispose();
			connectDone = null;
			sendDone?.Dispose();
			sendDone = null;
			receiveDone?.Dispose();
			receiveDone = null;
			// これでデストラクタは呼ばれなくなるので無駄な終了処理がなくなる
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// 接続コールバックメソッド
		/// </summary>
		/// <param name="ar"></param>
		private void ConnectCallback(IAsyncResult ar)
		{
			try
			{
				// これが非同期なConnectの完了
				((Socket)ar.AsyncState).EndConnect(ar);
				// 接続完了のイベントをセット
				connectDone.Set();
			}
			catch (Exception e)
			{
				logDelegate?.Invoke("接続コールバック例外," + e.Message);
				if (isMessage == true)
					_ = MessageBox.Show(e.Message, "接続コールバックメソッド 例外エラー", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		/// <summary>
		/// 受信コールバックメソッド
		/// </summary>
		/// <param name="ar"></param>
		private void ReceiveCallback(IAsyncResult ar)
		{
			try
			{
				// 保留中の非同期読み込みを終了
				_ = ((StateObject)ar.AsyncState).workSocket.EndReceive(ar);
				// 送信完了のイベントをセット
				receiveDone.Set();
			}
			catch (Exception e)
			{
				logDelegate?.Invoke("受信コールバック例外," + e.Message);
				if (isMessage == true)
					_ = MessageBox.Show(e.Message, "受信コールバックメソッド 例外エラー", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		/// <summary>
		/// 送信コールバックメソッド
		/// </summary>
		/// <param name="ar"></param>
		private void SendCallback(IAsyncResult ar)
		{
			// 送信完了のイベントをセット
			sendDone.Set();
		}

		/// <summary>
		/// データ送受信
		/// </summary>
		/// <param name="sendData">送信するデータの配列</param>
		/// <param name="receiveData">受信したデータを入れる配列の参照</param>
		/// <returns></returns>
		internal bool SendReceive(byte[] sendData, ref byte[] receiveData)
		{
			// 送信データのチェック
			if (sendData == null || sendData.Length == 0)
			{
				logDelegate?.Invoke("データ送信,送信するデータがありません。");
				if (isMessage == true)
					_ = MessageBox.Show("送信するデータがありません。", "データ送信", MessageBoxButton.OK, MessageBoxImage.Error);
				return false;
			}

			bool result = false;
			try
			{
				using Socket client = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
				try
				{
					// サーバへ接続
					_ = client.BeginConnect(new IPEndPoint(address, port), new AsyncCallback(ConnectCallback), client);
					// 接続が完了するまで待機
					if (connectDone.Wait(timeOut) == true)
					{
						// データ送信開始
						_ = client.BeginSend(sendData, 0, sendData.Length, 0, new AsyncCallback(SendCallback), client);
						
						// 送信が完了するまで待機
						if (sendDone.Wait(timeOut) == true)
						{
							// データ受信用Object作成
							StateObject state = new StateObject(ref receiveData) { workSocket = client };
							// 非同期受信開始
							_ = client.BeginReceive(state.buffer, 0, state.buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveCallback), state);
							// 受信が完了するまで待機
							if (receiveDone.Wait(timeOut) == true)
							{
								result = true;
							}
							else
							{
								logDelegate?.Invoke("データ送受信,データ受信タイムアウト。");
								if (isMessage == true)
									MessageBox.Show("データ受信タイムアウト", "データ送受信", MessageBoxButton.OK, MessageBoxImage.Error);
							}
						}
						else
						{
							logDelegate?.Invoke("データ送受信,送データ送信タイムアウト。");
							if (isMessage == true)
								_ = MessageBox.Show("データ送信タイムアウト", "データ送受信", MessageBoxButton.OK, MessageBoxImage.Error);
						}
					}
					else
					{
						logDelegate?.Invoke("データ送受信,サーバに接続できませんでした。");
						if (isMessage == true)
							_ = MessageBox.Show("サーバに接続できませんでした。", "データ送受信", MessageBoxButton.OK, MessageBoxImage.Error);
					}
				}
				catch (Exception e)
				{
					logDelegate?.Invoke("データ送受信例外," + e.Message);
					if (isMessage == true)
						_ = MessageBox.Show(e.Message, "例外エラー", MessageBoxButton.OK, MessageBoxImage.Error);
				}
				finally
				{
					//閉じる前の接続ソケットですべてのデータが送受信される
					client.Shutdown(SocketShutdown.Both);
					// ソケット接続を閉じ、ソケットを再利用できるようにする。再利用できる場合は true それ以外の場合は false
					client.Disconnect(true);
					connectDone.Reset();
					sendDone.Reset();
					receiveDone.Reset();
				}
			}
			catch (Exception e)
			{
				logDelegate?.Invoke("データ送受信例外," + e.Message);
				if (isMessage == true)
					_ = MessageBox.Show(e.Message, "例外エラー", MessageBoxButton.OK, MessageBoxImage.Error);
			}
			return result;
		}

		/// <summary>
		/// リモートデバイスからデータを受信するためのObject
		/// </summary>
		private class StateObject
		{
			/// <summary>
			/// クライアントソケット
			/// </summary>
			internal Socket workSocket { get; set; }

			/// <summary>
			/// 受信バッファ
			/// </summary>
			internal byte[] buffer { get; set; }

			/// <summary>
			/// コンストラクタ
			/// </summary>
			/// <param name="dataBuffer">受信データを格納する変数の参照</param>
			internal StateObject(ref byte[] dataBuffer)
			{
				buffer = dataBuffer;
			}
		}
	}
}
