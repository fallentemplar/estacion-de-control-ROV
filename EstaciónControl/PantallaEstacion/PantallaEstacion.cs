﻿using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using XInputDotNetPure;
using System.IO;
using System.Drawing.Imaging;
using System.Net.Sockets;
using log4net;
using EstacionControl.Dispositivos.Sensores;
using EstacionControl.ProcesamientoImagenes;
using System.Diagnostics;
using AForge.Video.VFW;
using EstacionControl.Ventanas;
using EstacionControl.Dispositivos;

namespace EstacionControl
{
    public partial class PantallaEstacion : Form
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        Giroscopio giroscopio;


        static readonly string rutaCapturas = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\EstacionControl\\Capturas\\";

        //Objetos para log y conectividad
        ConectividadRemota socketConector;
        ConectividadRemota socketReceptor;
        ControlXBOX controles;

        //Variables para comprobar conectividad
        bool control1_conectado;
        bool control2_conectado;
        bool raspberry_conectado;
        bool arduino_conectado;
        bool profTemp_conectado;
        bool giroscopio_conectado;
        public static bool recibiendo_video1;
        public static bool recibiendo_video2;
        public static bool recibiendo_video3;

        Color colorCampos;
        bool grabandoVideo1;
        bool grabandoVideo2;
        //bool grabandoVideo3;

        //Threads principales
        Thread actualizarControles;
        Thread dispositivosRemotos;
        Thread capturarVideo;
        Thread conexionConRaspberry;

        public PantallaEstacion()
        {
            log.Info("---------------------Iniciando ejecución-----------------------\n-------------------------------------------------------------------------------------------------------------");
            InitializeComponent();
            this.WindowState = FormWindowState.Maximized;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.UserPaint, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            grabandoVideo1 = false;
        }
        
        //Se inician múltiples hilos del programa y se ejecutan en segundo plano
        private void Form1_Load(object sender, EventArgs e)
        {
            this.Icon = EstacionControl.Properties.Resources.icono_tmmx_nuevo;
            colorCampos = indicador_temperatura.BackColor;

            socketConector = new ConectividadRemota(direccion_ip_texto.Text);
            socketReceptor = new ConectividadRemota(direccion_ip_texto.Text, 7001);
            controles = new ControlXBOX(socketConector,this);
            giroscopio = new Giroscopio(socketReceptor);

            //Hilo para manejar el control de XBOX ONE
            actualizarControles = new Thread(new ThreadStart(controles.ActualizarEstadoOrdenes)) { IsBackground = true };

            //Hilo de verificación de comunicación de dispositivos periféricos remotos
            dispositivosRemotos = new Thread(new ThreadStart(ComprobarDispositivosRemotos)) { IsBackground = true };
            dispositivosRemotos.Priority = ThreadPriority.AboveNormal;

            //Hilo de verificación de comunicación de dispositivos periféricos locales
            Thread verifConectividad = new Thread(new ThreadStart(ComprobarDispositivosLocales)) { IsBackground = true };
            verifConectividad.Start();

            PintarElementos();

            //-------------------------------------------//
            Giroscopio.mybitmap2.MakeTransparent(Color.Yellow); // Sets image transparency
            Giroscopio.mybitmap4.MakeTransparent(Color.Yellow); // Sets image transparency

            lista_camaras1.Click += Lista_camaras1_Click;
            lista_camaras2.Click += Lista_camaras2_Click;

            Camaras.InicializarCamaras();
            Camaras.AgregarCamarasIniciales();

            foreach(var camara in Camaras.listaCamaras)
            {
                lista_camaras1.Items.Add(camara.Value);
                lista_camaras2.Items.Add(camara.Value);
            }
            lista_camaras1.SelectedIndex = 0;
            lista_camaras2.SelectedIndex = 1;
        }

        private void Lista_camaras2_Click(object sender, EventArgs e)
        {
            lista_camaras2.Items.Clear();
            foreach (var camara in Camaras.listaCamaras)
            {
                lista_camaras2.Items.Add(camara.Value);
            }
            lista_camaras2.SelectedIndex = 1;
        }

        private void Lista_camaras1_Click(object sender, EventArgs e)
        {
            lista_camaras1.Items.Clear();
            foreach (var camara in Camaras.listaCamaras)
            {
                lista_camaras1.Items.Add(camara.Value);
            }
            lista_camaras1.SelectedIndex = 0;
        }

        //---------------------------------------------------------------------------------------------------------
        //Sección de comprobar conectividad
        //---------------------------------------------------------------------------------------------------------

        private delegate void delegado_control(bool estado);
        private delegate void delegado_raspberry(bool estado);
        private delegate void delegado_arduino(bool estado);
        private delegate void delegado_camara(bool estado);
        private delegate void delegado_profTemp(bool estado);
        private delegate void delegado_giroscopio(bool estado);

        public void ActualizarIndicadores(string indicador,bool estado)
        {
            switch(indicador)
            {
                case "linternas":
                    indicador_linternas.Invoke(new delegado_linternas(EncenderLinternas), estado);
                    break;
            }
        }

        void ComprobarDispositivosLocales()
        {
            while(true)
            {
                control1_conectado = controles.Estado_control(PlayerIndex.One);
                indicador_control1.Invoke(new delegado_control(Control1_conexion), control1_conectado);

                control2_conectado = controles.Estado_control(PlayerIndex.Two);
                indicador_control2.Invoke(new delegado_control(Control2_conexion), control2_conectado);
                Thread.Sleep(500);
            }
        }

        void ComprobarDispositivosRemotos()
        {
            try
            {
                while (true)
                {
                    log.Debug("Estoy verificando");
                    //raspberry_conectado = !((socketReceptor.cliente.Poll(1000, SelectMode.SelectRead) && (socketReceptor.cliente.Available == 0)) || !socketReceptor.cliente.Connected);
                    //socketReceptor.servidor.Connected;//socketReceptor.OperadorAND("servidor", (byte)socketReceptor.GetEstado());
                    indicador_raspberry.Invoke(new delegado_raspberry(Raspberry_conexion), raspberry_conectado);

                    arduino_conectado = socketReceptor.OperadorAND("arduino", (byte)socketReceptor.GetEstado());
                    indicador_arduino.Invoke(new delegado_arduino(Arduino_conexion), arduino_conectado);

                    profTemp_conectado = socketReceptor.OperadorAND("sensores", (byte)socketReceptor.GetEstado());
                    indicador_profundidad.Invoke(new delegado_profTemp(Sensores_conexion), profTemp_conectado);

                    giroscopio_conectado = socketReceptor.OperadorAND("acelerometro", (byte)socketReceptor.GetEstado());
                    giroscopio.PintarGiroscopio();
                    Thread.Sleep(500);
                }
            }
            catch (Exception)
            {

            }
        }

        void Control1_conexion(bool estado)
        {
            if (estado)
            {
                indicador_control1.Text = "Conectado";
                indicador_control1.BackColor = Color.Yellow;
            }
                
            else
            {
                indicador_control1.Text = "Desconectado";
                indicador_control1.BackColor = Color.Red;
            }
                
        }

        void Control2_conexion(bool estado)
        {
            if (estado)
            {
                indicador_control2.Text = "Conectado";
                indicador_control2.BackColor = Color.Yellow;
            }
            else
            {
                indicador_control2.Text = "Desconectado";
                indicador_control2.BackColor = Color.Red;
            }
                
        }

        void Arduino_conexion(bool estado)
        {
            if (estado)
            {
                indicador_arduino.Text = "Conectado";
                indicador_arduino.BackColor = Color.Yellow;
            }
            else
            {
                indicador_arduino.Text = "Desconectado";
                indicador_arduino.BackColor = Color.Red;
            }
                
        }

        void Raspberry_conexion(bool estado)
        {
            if (estado)
            {
                indicador_raspberry.Text = "Conectado";
                indicador_raspberry.BackColor = Color.Yellow;
            }
                
            else
            {
                indicador_raspberry.Text = "Desconectado";
                indicador_raspberry.BackColor = Color.Red;
            }        
        }

        /*void Camara_conexion(bool estado)
        {
            if (!camara1_desconectar.Enabled && !recibiendo_video1 && estado)
                camara1_conectar.Enabled = true;
            else
                camara1_conectar.Enabled = false;
        }*/
        
        void Sensores_conexion(bool estado)
        {
            indicador_temperatura.ForeColor = Color.Yellow;
            indicador_profundidad.ForeColor = Color.Yellow;
            if (estado)
            {
                indicador_temperatura.BackColor = colorCampos;
                indicador_profundidad.BackColor = colorCampos;
                indicador_temperatura.ForeColor = Color.Yellow;
                indicador_temperatura.Text = string.Format("{0:0.00}", (double)socketReceptor.getTemperatura())+ " ºC";
                indicador_profundidad.Text = string.Format("{0:0.00}", (double)socketReceptor.getProfundidad())+ " metros";
            }
            else
            {
                indicador_temperatura.BackColor = Color.Gray;
                indicador_profundidad.BackColor = Color.Gray;
                indicador_temperatura.Text = "N/A";
                indicador_profundidad.Text = "N/A";
            }
        }

        /////////////////Finaliza sección de conectividad/////////////
 
        private delegate void delegado_linternas(bool estado);

        
        //---------------------------------------------------------------------------------------------------------
        //La siguiente sección contiene el código de la Raspberry Pi
        //---------------------------------------------------------------------------------------------------------

        private void Camara_conectar_Clic(object sender, EventArgs e)
        {
            try
            {
                Thread obtenerStreamVideo = new Thread(new ThreadStart(Recibir_stream_video1));
                camara1_desconectar.Enabled = true;
                camara1_conectar.Enabled = false;

                obtenerStreamVideo.IsBackground = true;
                obtenerStreamVideo.Start();
                recibiendo_video1 = true;
                boton_fotografia1.Enabled = true;
                boton_video1.Enabled = true;
            }
            catch(Exception)
            {
                recibiendo_video1 = false;
            }
        }

        private void Camara_desconectar_Clic(object sender, EventArgs e)
        {
            camara1_desconectar.Enabled = false;
            camara1_conectar.Enabled = true;
            recibiendo_video1 = false;
            //textBox1.Enabled = true;
            if(visorCamara1.IsPlaying)
                visorCamara1.Stop();
            boton_fotografia1.Enabled = false;
            boton_video1.Enabled = false;
        }        

        delegate void delegado_video(string url);
        delegate string delegado_IP(int numeroCamara);

        private string RecibirIP(int numeroCamara)
        {
            switch(numeroCamara)
            {
                case 1:
                    return lista_camaras1.SelectedItem.ToString();
                case 2:
                    return lista_camaras2.SelectedItem.ToString();
                default:
                    return "";
            }
        }

        private void Recibir_stream_video1()
        {
            string ipCamara = lista_camaras1.Invoke(new delegado_IP(RecibirIP),1).ToString();
            visorCamara1.Invoke(new delegado_video(MostrarVideo1), "http://" + ipCamara);
        }
        private void Recibir_stream_video2()
        {
            string ipCamara = lista_camaras2.Invoke(new delegado_IP(RecibirIP), 2).ToString();
            visorCamara2.Invoke(new delegado_video(MostrarVideo2), "http://" + ipCamara);
        }

        private void MostrarVideo1(string URL_video)
        {
            visorCamara1.StartPlay(new Uri(URL_video)); //TODO: configurar TCP o UDP
        }
        private void MostrarVideo2(string URL_video)
        {
            visorCamara2.StartPlay(new Uri(URL_video)); //TODO: configurar TCP o UDP
        }
        private void MostrarVideo3(string URL_video)
        {
            //visorCamara3.StartPlay(new Uri(URL_video)); //TODO: configurar TCP o UDP
        }

        private void PintarElementos()
        {
            indicador_control1.BackColor = Color.Red;
            indicador_control2.BackColor = Color.Red;
            indicador_raspberry.BackColor = Color.Red;
            indicador_arduino.BackColor = Color.Red;

            indicador_temperatura.ForeColor = Color.Yellow;
            indicador_profundidad.ForeColor = Color.Yellow;
            indicador_inductivo.ForeColor = Color.Yellow;
            indicador_ph.ForeColor = Color.Yellow;
        }

        private void EncenderLinternas(bool estado)
        {
            if (estado)
                indicador_linternas.BackColor = Color.Green;
            else
                indicador_linternas.BackColor = Color.Gray;
        }

        private void Boton_fotografia_Click(object sender, EventArgs e) //Método para tomar fotografía y almacenarla en disco
        {
            Bitmap foto = Camaras.CapturarImagen(visorCamara1);
            Camaras.TomarFotografia(foto);
        }

        

        private void Cerrar(object sender, FormClosingEventArgs e)
        {
            //socketConector.CerrarConexion();
            Desconectar();
        }

        private void ComprobarRaspberry()
        {
            while (true)
            {
                try
                {
                    while (true)
                    {
                        log.Debug("Test raspbery");
                        raspberry_conectado = socketConector.Ping(direccion_ip_texto.Text);
                        Thread.Sleep(200);
                    }
                }
                catch (Exception) { }
            }   
        }

        private void Boton_Conectar_Click(object sender, EventArgs e)
        {
            if (!socketConector.conexionRealizada)
            {
                try
                {
                    if (socketConector.Conectar(direccion_ip_texto.Text))
                    {
                        socketReceptor.Conectar(direccion_ip_texto.Text, 7001);
                        SolicitarDatos();
                        giroscopioToolStripMenuItem.Enabled = true;
                        visorDeCámaraToolStripMenuItem.Enabled = true;
                        camara1_conectar.Enabled = true; //Habilita la posibilidad de iniciar recepción de video.
                        direccion_ip_texto.Enabled = false;
                        puerto_texto.Enabled = false;

                        camara2_conectar.Enabled = true;
                        actualizarControles.Start();
                        dispositivosRemotos.IsBackground = true;
                        dispositivosRemotos.Start();

                        boton_Conectar.Text = "Desconectar";

                        conexionConRaspberry = new Thread(new ThreadStart(ComprobarRaspberry)) { IsBackground = true }; //Verifica 
                        conexionConRaspberry.Priority = ThreadPriority.Highest;
                        conexionConRaspberry.Start();
                    }
                    else
                        MessageBox.Show("No se pudo establecer la conexión remota", "Error de conexión",
                                MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
                catch (SocketException)
                {
                    log.Error("No se pudo establecer la conexión");
                    MessageBox.Show("No se pudo establecer la conexión remota", "Error de conexión",
                                MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
            }
            else
            {
                DialogResult confirmacion = MessageBox.Show("¿Desea desconectar?","Confirmación",MessageBoxButtons.YesNo);
                if(confirmacion == DialogResult.Yes)
                {
                    boton_Conectar.Text = "¡Conectar!";
                    Desconectar();
                    
                }
            }
            
        }

        private void DetenerRecepcionVideo()
        {
            if(visorCamara1.IsPlaying)
            {
                visorCamara1.Stop();
                grabandoVideo1 = false;
                boton_video1.Image = EstacionControl.Properties.Resources.video_1_micro;
            }
            if (visorCamara2.IsPlaying)
            {
                visorCamara2.Stop();
                grabandoVideo2 = false;
                boton_video2.Image = EstacionControl.Properties.Resources.video_1_micro;
            }
                
        }

        private void Desconectar()
        {
            socketConector.CerrarConexion();
            socketReceptor.CerrarConexion();
            giroscopioToolStripMenuItem.Enabled = true;
            camara1_conectar.Enabled = false; //Habilita la posibilidad de iniciar recepción de video.
            direccion_ip_texto.Enabled = true;
            puerto_texto.Enabled = true;

            camara2_conectar.Enabled = false;
            if (actualizarControles != null && actualizarControles.IsAlive) 
                actualizarControles.Interrupt();
            if (dispositivosRemotos != null && dispositivosRemotos.IsAlive) 
                dispositivosRemotos.Interrupt();
            DetenerRecepcionVideo();
            if (conexionConRaspberry != null && conexionConRaspberry.IsAlive)
                conexionConRaspberry.Interrupt();
            socketConector.conexionRealizada = false;
        }

        private void SolicitarDatos()
        {
            log.Debug("Llamada a método [solicitarDatos]");
            socketReceptor.SolicitarRecepcion();
        }

        private void AcercaDeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AcercaDe a = new AcercaDe();
            a.ShowDialog();
        }

        private void Boton_generarQR_Click(object sender, EventArgs e)
        {
            CodigoQR qr = new CodigoQR(lista_camaras1.SelectedItem.ToString());
            qr.ShowDialog();
        }

        private void giroscopioToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Giroscopio giro = new Giroscopio(socketConector);
            giro.Show();
        }

        private void abrirCarpetaDeFotografíasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", rutaCapturas);
        }

        private void botonReconociento_Click(object sender, EventArgs e)
        {
            Stopwatch a = new Stopwatch();
            ClasificacionFiguras clasificador = new ClasificacionFiguras();
            
            Bitmap bitmap = Camaras.CapturarImagen(visorCamara1);
            if(bitmap != null)
            {
                a.Start();
                bitmap = BitMaps.DrawAsNegative(bitmap);
                EspeciesReconocidas especiesReconocidas = new EspeciesReconocidas(clasificador.ProcesarImagen(bitmap),cantidadFiguras);
                a.Stop();
                especiesReconocidas.ShowDialog();
                log.Info("Tiempo de identificación: " + (double)a.ElapsedMilliseconds / 1000);
            }
        }

        public static int[] cantidadFiguras = new int[] { 0, 0, 0, 0};//circulo,cuadrado,rectangulo,triangulo

        private void boton_video_Click(object sender, EventArgs e)
        {
            log.Info("Capturando video");
            capturarVideo = new Thread(new ThreadStart(GrabarVideo)) { IsBackground = true };
            if (!grabandoVideo1)
            {
                boton_video1.Image = EstacionControl.Properties.Resources.stop_micro;
                grabandoVideo1 = true;
                capturarVideo.Start();
            }
            else
            {
                boton_video1.Image = EstacionControl.Properties.Resources.video_1_micro;
                grabandoVideo1 = false;
                capturarVideo.Abort();
            }
        }

        private void GrabarVideo()
        {
            AVIWriter grabadorVideo = new AVIWriter();
            try
            {
                Bitmap imagen = Camaras.CapturarImagen(visorCamara1);
                DateTime Hoy = DateTime.Now;
                string fecha_actual = Hoy.ToString("dd-MM-yyyy HH-mm-ss");
                grabadorVideo.Open(rutaCapturas + "\\video_" + fecha_actual + ".avi", imagen.Width, imagen.Height);
                while (true)
                {
                    if (grabandoVideo1)
                    {
                        grabadorVideo.AddFrame(Camaras.CapturarImagen(visorCamara1));
                        Thread.Sleep(50);
                    }
                    else
                        break;
                }
                grabadorVideo.Close();
            }
            catch (Exception)
            {
                grabadorVideo.Close();
            }
        }

        private void configuraciónDeCámarasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CamaraPanel panelCamaras = new CamaraPanel();
            panelCamaras.ShowDialog();
        }

        private void camara2_conectar_Click(object sender, EventArgs e)
        {
            try
            {
                Thread obtenerStreamVideo = new Thread(new ThreadStart(Recibir_stream_video2));
                camara2_desconectar.Enabled = true;
                camara2_conectar.Enabled = false;

                obtenerStreamVideo.IsBackground = true;
                obtenerStreamVideo.Start();
                recibiendo_video2 = true;
                boton_fotografia2.Enabled = true;
                boton_video2.Enabled = true;
            }
            catch (Exception)
            {
                recibiendo_video2 = false;
            }
        }

        private void camara2_desconectar_Click(object sender, EventArgs e)
        {
            camara2_desconectar.Enabled = false;
            camara2_conectar.Enabled = true;
            recibiendo_video2 = false;
            if (visorCamara2.IsPlaying)
                visorCamara2.Stop();
            boton_fotografia2.Enabled = false;
            boton_video2.Enabled = false;
        }

        private void visorDeCámaraToolStripMenuItem_Click(object sender, EventArgs e)
        {
            VisorCamara visorCamara = new VisorCamara();
            visorCamara.Show();
        }
    }
}