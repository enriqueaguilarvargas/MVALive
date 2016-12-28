using System;
using UIKit;
using System.Drawing;
using System.Threading.Tasks;
using AVFoundation;
using Foundation;
using System.IO;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using CoreLocation;
using MapKit;
using Plugin.Geolocator;
using LocalAuthentication;
using ObjCRuntime;

namespace MVAV41
{
	public partial class ViewController : UIViewController
	{
		string archivoLocal;
		AVCaptureDevice dispositivodeCaptura;
		AVCaptureSession sesiondeCaptura;
		AVCaptureDeviceInput entradaDispositivo;
		AVCaptureStillImageOutput salidaImagen;
		AVCaptureVideoPreviewLayer preview;
		string ruta;
		string Pais, Ciudad;
		double latitud, longitud;
		byte[] arregloJpg;
		CLLocationManager locationManager;
		NSError Error;
		protected ViewController(IntPtr handle) : base(handle)
		{
		}
		public override async void ViewDidLoad()
		{
			base.ViewDidLoad();

			try
			{
				var verifica = new LocalAuthentication.LAContext();
				var auth = await verifica.EvaluatePolicyAsync
				                         (LAPolicy.
												  DeviceOwnerAuthenticationWithBiometrics,
											"Autenticación para usar la Aplicación");
				if (auth.Item1)
				{
					await autorizacionCamara();
					ConfiguracionCamara();
				}
				else
				{
					Selector selector = new Selector("terminateWithSuccess");
					UIApplication.SharedApplication.PerformSelector
						(selector, UIApplication.SharedApplication, 0);
				}
			}
			catch (NSErrorException ex)
			{
				var reason = Convert.ToInt16(ex.Code);
				var status = (LAStatus)reason;
				MessageBox("Status", status.ToString());
			}


			btnCapturar.TouchUpInside += async delegate
			{
				var salidadevideo = salidaImagen.ConnectionFromMediaType(AVMediaType.Video);
				var bufferdevideo = await salidaImagen.CaptureStillImageTaskAsync(salidadevideo);
				var datosImagen = AVCaptureStillImageOutput.JpegStillToNSData(bufferdevideo);

				arregloJpg = datosImagen.ToArray();
				string rutacarpeta = Environment.GetFolderPath
												  (Environment.SpecialFolder.Personal);
				string resultado = "Foto";
				archivoLocal = resultado + ".jpg";
				ruta = Path.Combine(rutacarpeta, archivoLocal);
				File.WriteAllBytes(ruta, arregloJpg);
				Imagen.Image = UIImage.FromFile(ruta);
			};
			btnRespaldar.TouchUpInside += async delegate
			{
				try
				{
					CloudStorageAccount cuentaAlmacenamiento = CloudStorageAccount.Parse
						("DefaultEndpointsProtocol=https;AccountName=almacenamientoxamarin;AccountKey=hX6T/p8IcOAF8RomLimw0fnLfkUC5CbnLOEn+6X5xLo3BxvOrmsUel0U2B4UtSK8cONvkBWUAFNJT+OR5tc3EA==");
					CloudBlobClient clienteBlob = cuentaAlmacenamiento.CreateCloudBlobClient();
					CloudBlobContainer contenedor = clienteBlob.GetContainerReference("imagenes");
					CloudBlockBlob recursoblob = contenedor.GetBlockBlobReference(archivoLocal);
					await recursoblob.UploadFromFileAsync(ruta);
					MessageBox("Guardado en", "Azure Storage - Blob");

					CloudTableClient tableClient = cuentaAlmacenamiento.CreateCloudTableClient();

					CloudTable table = tableClient.GetTableReference("Ubicaciones");

					await table.CreateIfNotExistsAsync();

					UbicacionEntity ubica = new UbicacionEntity(archivoLocal, Pais);
					ubica.Latitud = latitud;
					ubica.Localidad = Ciudad;
					ubica.Longitud = longitud;

					TableOperation insertar = TableOperation.Insert(ubica);
					await table.ExecuteAsync(insertar);

					MessageBox("Guardado en Azure", "Table NoSQL");

				}
				catch (StorageException ex)
				{
					MessageBox("Error: ", ex.Message);
				}
			};

			#region "Controles de la Cámara"
			slider2.MinValue = 1000f;
			slider2.MaxValue = 10000f;
			slider2.ValueChanged += CambiodeValor;

			slider3.MinValue = -150f;
			slider3.MaxValue = 150f;
			slider3.ValueChanged += CambiodeValor;

			slider1.MinValue = dispositivodeCaptura.ActiveFormat.MinISO;
			slider1.MaxValue = dispositivodeCaptura.ActiveFormat.MaxISO;
			slider1.ValueChanged += (sender, e) =>
			{
				if (dispositivodeCaptura.LockForConfiguration(out Error))
				{
					dispositivodeCaptura.LockExposure
										(dispositivodeCaptura.ExposureDuration, slider1.Value, null);
					dispositivodeCaptura.UnlockForConfiguration();
				}
			};
			#endregion

			#region "Mapas"

			locationManager = new CLLocationManager();
			locationManager.RequestWhenInUseAuthorization();
			Mapa.ShowsUserLocation = true;
			var locator = CrossGeolocator.Current;
			var position = await
				locator.GetPositionAsync(timeoutMilliseconds: 10000);

			Mapa.MapType = MKMapType.Hybrid;
			CLLocationCoordinate2D Centrar = new CLLocationCoordinate2D
				(position.Latitude,
				 position.Longitude);
			MKCoordinateSpan Altura = new MKCoordinateSpan(.002, .002);
			MKCoordinateRegion Region = new MKCoordinateRegion
				(Centrar, Altura);
			Mapa.SetRegion(Region, true);

			CLLocation Ubicacion = new CLLocation(position.Latitude, position.Longitude);

			CLGeocoder clg = new CLGeocoder();
			var Datos = await clg.ReverseGeocodeLocationAsync(Ubicacion);
			Pais = Datos[0].Country;
			Ciudad = Datos[0].Locality;
			latitud = position.Latitude;
			longitud = position.Longitude;

			#endregion
		}


		#region "ControlesdelaCamara"
		void CambiodeValor(object sender, EventArgs e)
		{
			var TempyTinta = new AVCaptureWhiteBalanceTemperatureAndTintValues
					(slider2.Value, slider3.Value);
			var estatus = dispositivodeCaptura.GetDeviceWhiteBalanceGains(TempyTinta);

			if (dispositivodeCaptura.LockForConfiguration(out Error))
			{
				estatus = Ajuste(estatus);
				dispositivodeCaptura.SetWhiteBalanceModeLockedWithDeviceWhiteBalanceGains
					   (estatus, null);
				dispositivodeCaptura.UnlockForConfiguration();
			}
			slider2.BeginInvokeOnMainThread(() =>
			{
				slider2.Value = TempyTinta.Temperature;
			});
			slider3.BeginInvokeOnMainThread(() =>
			{
				slider3.Value = TempyTinta.Tint;
			});
		}
		AVCaptureWhiteBalanceGains Ajuste(AVCaptureWhiteBalanceGains valores)
		{
			valores.RedGain = Math.Max(1, valores.RedGain);
			valores.BlueGain = Math.Max(1, valores.BlueGain);
			valores.GreenGain = Math.Max(1, valores.GreenGain);

			float valormaximo = dispositivodeCaptura.MaxWhiteBalanceGain;

			valores.RedGain = Math.Min(valormaximo, valores.RedGain);
			valores.BlueGain = Math.Min(valormaximo, valores.BlueGain);
			valores.GreenGain = Math.Min(valormaximo, valores.GreenGain);
			return valores;
		}
		#endregion



		#region Camara
		async Task autorizacionCamara()
		{
			var estatus = AVCaptureDevice.GetAuthorizationStatus(AVMediaType.Video);
			if (estatus != AVAuthorizationStatus.Authorized)
			{
				await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVMediaType.Video);
			}
		}
		public void ConfiguracionCamara()
		{
			sesiondeCaptura = new AVCaptureSession();
			preview = new AVCaptureVideoPreviewLayer(sesiondeCaptura)
			{
				Frame = new RectangleF(40, 50, 300, 350)
			};
			View.Layer.AddSublayer(preview);
			dispositivodeCaptura = AVCaptureDevice.DefaultDeviceWithMediaType(AVMediaType.Video);
			entradaDispositivo = AVCaptureDeviceInput.FromDevice(dispositivodeCaptura);
			sesiondeCaptura.AddInput(entradaDispositivo);
			salidaImagen = new AVCaptureStillImageOutput()
			{
				OutputSettings = new NSDictionary()
			};
			sesiondeCaptura.AddOutput(salidaImagen);
			sesiondeCaptura.StartRunning();
		}
		#endregion

		public static void MessageBox(string Title, string message)
		{
			var Alerta = new UIAlertView();
			Alerta.Title = Title;
			Alerta.Message = message;
			Alerta.AddButton("OK");
			Alerta.Show();
		}
	}
	public class UbicacionEntity : TableEntity
	{
		public UbicacionEntity(string Archivo, string Pais)
		{
			this.PartitionKey = Archivo;
			this.RowKey = Pais;
		}
		public UbicacionEntity() { }
		public double Latitud { get; set; }
		public double Longitud { get; set; }
		public string Localidad { get; set; }
	}

}