using System;
using ImGuiNET;
using UImGui.Assets;
using UImGui.Events;
using UImGui.Platform;
using UImGui.Renderer;
using UnityEngine;
using UnityEngine.Rendering;


namespace UImGui
{
	public class ImGuiManager : ScriptableObject
	{
        private static ImGuiManager s_Instance;

        public static ImGuiManager instance
        {
            get
            {
                if ((UnityEngine.Object)s_Instance == (UnityEngine.Object)null)
                {
                    ImGuiManager val = ScriptableObject.CreateInstance<ImGuiManager>();
                    val.hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;
                    // Note: The constructor that is executed via CreateInstance()
                    // will set s_Instance to val
                }

                return s_Instance;
            }
        }

        protected ImGuiManager()
        {
            if ((UnityEngine.Object)s_Instance != (UnityEngine.Object)null)
            {
                Debug.LogError("ImGuiManager already exists. Did you query the singleton in a constructor?");
            }
            else
            {
                s_Instance = this;
            }
        }




		private Context _context;
		private IRenderer _renderer;
		private IPlatform _platform;
		private CommandBuffer _renderCommandBuffer;

		private Camera _camera = null;
		private Camera m_incomingCamera = null;

		private RenderImGui _renderFeature = null;

		private RenderType _rendererType = RenderType.Mesh;

        /// <summary>
        /// We use InputSystem package versus legacy input system so that we can use ImGui outside of play mode
        /// </summary>
		private InputType _platformType = InputType.InputSystem;

		private IniSettingsAsset _iniSettings = null;

		public UIOConfig Config => _initialConfiguration;
		private UIOConfig _initialConfiguration = new UIOConfig
		{
			ImGuiConfig = ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable,

			DoubleClickTime = 0.30f,
			DoubleClickMaxDist = 6.0f,

			DragThreshold = 6.0f,

			KeyRepeatDelay = 0.250f,
			KeyRepeatRate = 0.050f,

			FontGlobalScale = 1.0f,
			FontAllowUserScaling = false,

			DisplayFramebufferScale = Vector2.one,

			MouseDrawCursor = false,
			TextCursorBlink = false,

			ResizeFromEdges = true,
			MoveFromTitleOnly = true,
			ConfigMemoryCompactTimer = 1f,
		};

		private FontInitializerEvent _fontCustomInitializer = new FontInitializerEvent();

		private FontAtlasConfigAsset _fontAtlasConfiguration = null;

		private ShaderResourcesAsset _shaders = null;

		private StyleAsset _style = null;

		private CursorShapesAsset _cursorShapes = null;

		private bool _doGlobalEvents = true; // Do global/default Layout event too.

		private bool m_performReload = false;

		#region Events
		public event System.Action<ImGuiManager> Layout;
		public event System.Action<ImGuiManager> OnInitialize;
		public event System.Action<ImGuiManager> OnDeinitialize;
		#endregion


        /// <summary>
        /// This will be called exactly once before Awake(). 
        /// It will not be executed again if an assembly reload occurs
        /// </summary>
        void Reset()
        {
        }

        /// <summary>
        /// This will happen exactly once when this singleton instance is created
        /// It will not be executed again if an assembly reload occurs
        /// </summary>
        void Awake()
        {
        }

        void OnDestroy()
        {
			UImGuiUtility.DestroyContext(_context);
			if (s_Instance == this)
			{
				s_Instance = null;
			}
        }

        /// <summary>
        /// This will get called when an assembly reload is about to occur
        /// </summary>
        void OnDisable()
        {
            DeInitialize();
        }

        /// <summary>
        /// This will get called after Awake() upon first time initialization. It will
        /// also be called after an assembly reload occurs
        /// </summary>
        void OnEnable()
        {
			_context = UImGuiUtility.CreateContext();

			_shaders = Resources.Load("DefaultShader") as ShaderResourcesAsset;
			_style = Resources.Load("DefaultStyle") as StyleAsset;
			_cursorShapes = Resources.Load("DefaultCursorShape") as CursorShapesAsset;

            Camera camera = _camera ? _camera : Camera.main;
            if (camera)
            {
                SetCamera(camera);
            }
        }

		public void SetUserData(System.IntPtr userDataPtr)
		{
			_initialConfiguration.UserData = userDataPtr;
			ImGuiIOPtr io = ImGui.GetIO();
			_initialConfiguration.ApplyTo(io);
		}

		public void SetCamera(Camera camera)
		{
            m_incomingCamera = camera;
			m_performReload = true;
		}

		public void PerformReload()
		{
			m_performReload = true;
		}

		private void Initialize()
		{
			void Fail(string reason)
			{
				throw new System.Exception($"Failed to start: {reason}.");
			}

			if (_camera == null)
			{
				Fail(nameof(_camera));
			}

			if (_renderFeature == null && RenderUtility.IsUsingURP())
			{
				Fail(nameof(_renderFeature));
			}

			_renderCommandBuffer = RenderUtility.GetCommandBuffer(Constants.UImGuiCommandBuffer);

			if (RenderUtility.IsUsingURP())
			{
#if HAS_URP
				_renderFeature.Camera = _camera;
#endif
				_renderFeature.CommandBuffer = _renderCommandBuffer;
			}
			else if (!RenderUtility.IsUsingHDRP())
			{
				_camera.AddCommandBuffer(CameraEvent.AfterEverything, _renderCommandBuffer);
			}

			UImGuiUtility.SetCurrentContext(_context);

			ImGuiIOPtr io = ImGui.GetIO();

			_initialConfiguration.ApplyTo(io);
			_style?.ApplyTo(ImGui.GetStyle());

			_context.TextureManager.BuildFontAtlas(io, _fontAtlasConfiguration, _fontCustomInitializer);
			_context.TextureManager.Initialize(io);

			IPlatform platform = PlatformUtility.Create(_platformType, _cursorShapes, _iniSettings);
			SetPlatform(platform, io);
			if (_platform == null)
			{
				Fail(nameof(_platform));
			}

			SetRenderer(RenderUtility.Create(_rendererType, _shaders, _context.TextureManager), io);
			if (_renderer == null)
			{
				Fail(nameof(_renderer));
			}

			if (_doGlobalEvents)
			{
//XXX				UImGuiUtility.DoOnInitialize(this);
			}
			OnInitialize?.Invoke(this);
		}

		private void DeInitialize()
		{
			UImGuiUtility.SetCurrentContext(_context);
			ImGuiIOPtr io = ImGui.GetIO();

			SetRenderer(null, io);
			SetPlatform(null, io);

			UImGuiUtility.SetCurrentContext(null);

            if (_context != null)
            {
                _context.TextureManager.Shutdown();
                _context.TextureManager.DestroyFontAtlas(io);
            }

			if (RenderUtility.IsUsingURP())
			{
				if (_renderFeature != null)
				{
#if HAS_URP
					_renderFeature.Camera = null;
#endif
					_renderFeature.CommandBuffer = null;
				}
			}
			else if(!RenderUtility.IsUsingHDRP())
			{
				if (_camera != null)
				{
                    if (_renderCommandBuffer != null)
                    {
                        _camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _renderCommandBuffer);
                    }
				}
			}

			if (_renderCommandBuffer != null)
			{
				RenderUtility.ReleaseCommandBuffer(_renderCommandBuffer);
    			_renderCommandBuffer = null;
			}

			if (_doGlobalEvents)
			{
//XXX				UImGuiUtility.DoOnDeinitialize(this);
			}
			OnDeinitialize?.Invoke(this);
		}

		public void Update()
		{
            if (!RenderUtility.IsUsingHDRP())
            {
                if ((_renderCommandBuffer != null) && (_renderer != null) && (_camera))
                {
                    DoUpdate(_renderCommandBuffer);
                }
            }

			if (m_performReload)
			{
				m_performReload = false;
                DeInitialize();
                if (m_incomingCamera)
                {
                    _camera = m_incomingCamera;
                    m_incomingCamera = null;
                }
                if (_camera)
                {
                    Initialize();
                }
			}
		}

		internal void DoUpdate(CommandBuffer buffer)
		{
			UImGuiUtility.SetCurrentContext(_context);
			ImGuiIOPtr io = ImGui.GetIO();

			Constants.PrepareFrameMarker.Begin(this);
			_context.TextureManager.PrepareFrame(io);
			_platform.PrepareFrame(io, _camera.pixelRect);
			ImGui.NewFrame();
#if !UIMGUI_REMOVE_IMGUIZMO
			ImGuizmoNET.ImGuizmo.BeginFrame();
#endif
			Constants.PrepareFrameMarker.End();

			Constants.LayoutMarker.Begin(this);
			try
			{
				if (_doGlobalEvents)
				{
//XXX					UImGuiUtility.DoLayout(this);
				}

				Layout?.Invoke(this);
			}
			finally
			{
				ImGui.Render();
				Constants.LayoutMarker.End();
			}

			Constants.DrawListMarker.Begin(this);
			_renderCommandBuffer.Clear();
			_renderer.RenderDrawLists(buffer, ImGui.GetDrawData());
			Constants.DrawListMarker.End();
		}

		private void SetRenderer(IRenderer renderer, ImGuiIOPtr io)
		{
			_renderer?.Shutdown(io);
			_renderer = renderer;
			_renderer?.Initialize(io);
		}

		private void SetPlatform(IPlatform platform, ImGuiIOPtr io)
		{
			_platform?.Shutdown(io);
			_platform = platform;
			_platform?.Initialize(io, _initialConfiguration, "Unity " + _platformType.ToString());
		}
	}
}