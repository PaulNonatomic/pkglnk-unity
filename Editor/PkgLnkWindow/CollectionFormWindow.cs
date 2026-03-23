using System;
using Nonatomic.PkgLnk.Editor.Api;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Modal EditorWindow for creating or editing a collection.
	/// </summary>
	public class CollectionFormWindow : EditorWindow
	{
		private const int WindowWidth = 400;
		private const int WindowHeight = 340;
		private const float SlugCheckDelay = 0.4f;

		private bool _isEditMode;
		private CollectionData _existing;
		private Action<CollectionData> _onCompleted;

		// Form section
		private VisualElement _formSection;
		private TextField _nameField;
		private TextField _slugField;
		private Label _slugAvailabilityLabel;
		private TextField _descriptionField;
		private Label _errorLabel;
		private Button _submitButton;
		private Button _cancelButton;

		// Saving section
		private VisualElement _savingSection;

		// Slug check state
		private double _slugCheckScheduledTime;
		private string _pendingSlugCheck;
		private bool _slugAvailable;
		private bool _slugChecking;
		private bool _nameEdited;

		/// <summary>Opens the window in create mode.</summary>
		public static void ShowCreate(Action<CollectionData> onCreated)
		{
			var wnd = CreateInstance<CollectionFormWindow>();
			wnd._isEditMode = false;
			wnd._onCompleted = onCreated;
			wnd.titleContent = new GUIContent("Create Collection");
			wnd.minSize = new Vector2(WindowWidth, WindowHeight);
			wnd.maxSize = new Vector2(WindowWidth, WindowHeight);
			wnd.ShowUtility();
			wnd.CenterOnScreen();
		}

		/// <summary>Opens the window in edit mode with existing data.</summary>
		public static void ShowEdit(CollectionData existing, Action<CollectionData> onUpdated)
		{
			var wnd = CreateInstance<CollectionFormWindow>();
			wnd._isEditMode = true;
			wnd._existing = existing;
			wnd._onCompleted = onUpdated;
			wnd.titleContent = new GUIContent("Edit Collection");
			wnd.minSize = new Vector2(WindowWidth, WindowHeight);
			wnd.maxSize = new Vector2(WindowWidth, WindowHeight);
			wnd.ShowUtility();
			wnd.CenterOnScreen();
		}

		private void CenterOnScreen()
		{
			var screenWidth = Screen.currentResolution.width;
			var screenHeight = Screen.currentResolution.height;
			var x = (screenWidth - WindowWidth) / 2;
			var y = (screenHeight - WindowHeight) / 2;
			position = new Rect(x, y, WindowWidth, WindowHeight);
		}

		public void CreateGUI()
		{
			var root = rootVisualElement;

			var baseSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles.uss");
			if (baseSheet != null) root.styleSheets.Add(baseSheet);

			var themeSuffix = EditorGUIUtility.isProSkin ? "Dark" : "Light";
			var themeSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
				$"Packages/com.nonatomic.pkglnk/Editor/PkgLnkWindow/PkgLnkWindowStyles{themeSuffix}.uss");
			if (themeSheet != null) root.styleSheets.Add(themeSheet);

			root.AddToClassList("pkglnk-window");
			root.AddToClassList("collection-form-root");

			BuildFormSection(root);
			BuildSavingSection(root);

			_savingSection.style.display = DisplayStyle.None;

			if (_isEditMode && _existing != null)
			{
				_nameField.value = _existing.name;
				_slugField.value = _existing.slug;
				_slugField.SetEnabled(false);
				_descriptionField.value = _existing.description ?? string.Empty;
				_slugAvailable = true;
				_nameEdited = true;
			}
		}

		private void BuildFormSection(VisualElement root)
		{
			_formSection = new VisualElement();
			_formSection.AddToClassList("collection-form-section");
			root.Add(_formSection);

			// Title
			var title = new Label(_isEditMode ? "Edit Collection" : "Create Collection");
			title.AddToClassList("collection-form-title");
			_formSection.Add(title);

			// Name
			var nameGroup = new VisualElement();
			nameGroup.AddToClassList("collection-form-field");
			_formSection.Add(nameGroup);

			var nameLabel = new Label("Name");
			nameLabel.AddToClassList("collection-form-label");
			nameGroup.Add(nameLabel);

			_nameField = new TextField { maxLength = 100 };
			_nameField.AddToClassList("collection-form-input");
			_nameField.RegisterValueChangedCallback(OnNameChanged);
			nameGroup.Add(_nameField);

			// Slug
			var slugGroup = new VisualElement();
			slugGroup.AddToClassList("collection-form-field");
			_formSection.Add(slugGroup);

			var slugRow = new VisualElement();
			slugRow.AddToClassList("collection-form-slug-row");
			slugGroup.Add(slugRow);

			var slugLabel = new Label("Slug");
			slugLabel.AddToClassList("collection-form-label");
			slugRow.Add(slugLabel);

			_slugAvailabilityLabel = new Label();
			_slugAvailabilityLabel.AddToClassList("slug-availability-label");
			slugRow.Add(_slugAvailabilityLabel);

			_slugField = new TextField { maxLength = 60 };
			_slugField.AddToClassList("collection-form-input");
			_slugField.RegisterValueChangedCallback(OnSlugChanged);
			slugGroup.Add(_slugField);

			// Description
			var descGroup = new VisualElement();
			descGroup.AddToClassList("collection-form-field");
			_formSection.Add(descGroup);

			var descLabel = new Label("Description");
			descLabel.AddToClassList("collection-form-label");
			descGroup.Add(descLabel);

			_descriptionField = new TextField { maxLength = 500, multiline = true };
			_descriptionField.AddToClassList("collection-form-input");
			_descriptionField.AddToClassList("collection-form-textarea");
			descGroup.Add(_descriptionField);

			// Error
			_errorLabel = new Label();
			_errorLabel.AddToClassList("collection-form-error");
			_errorLabel.style.display = DisplayStyle.None;
			_formSection.Add(_errorLabel);

			// Buttons
			var buttonRow = new VisualElement();
			buttonRow.AddToClassList("collection-form-buttons");
			_formSection.Add(buttonRow);

			_cancelButton = new Button(() => Close()) { text = "Cancel" };
			_cancelButton.AddToClassList("collection-form-cancel");
			buttonRow.Add(_cancelButton);

			_submitButton = new Button(OnSubmit) { text = _isEditMode ? "Save" : "Create" };
			_submitButton.AddToClassList("collection-form-submit");
			buttonRow.Add(_submitButton);
		}

		private void BuildSavingSection(VisualElement root)
		{
			_savingSection = new VisualElement();
			_savingSection.AddToClassList("collection-form-saving");
			root.Add(_savingSection);

			var savingLabel = new Label(_isEditMode ? "Saving..." : "Creating...");
			savingLabel.AddToClassList("collection-form-saving-label");
			_savingSection.Add(savingLabel);
		}

		private void OnNameChanged(ChangeEvent<string> evt)
		{
			_nameEdited = true;

			if (!_isEditMode)
			{
				_slugField.value = GenerateSlug(evt.newValue);
			}
		}

		private void OnSlugChanged(ChangeEvent<string> evt)
		{
			if (_isEditMode) return;

			// Force lowercase and valid characters
			var cleaned = GenerateSlug(evt.newValue);
			if (cleaned != evt.newValue)
			{
				_slugField.SetValueWithoutNotify(cleaned);
			}

			ScheduleSlugCheck(cleaned);
		}

		private void ScheduleSlugCheck(string slug)
		{
			if (string.IsNullOrEmpty(slug) || slug.Length < 3)
			{
				_slugAvailabilityLabel.text = string.Empty;
				_slugAvailable = false;
				return;
			}

			_pendingSlugCheck = slug;
			_slugCheckScheduledTime = EditorApplication.timeSinceStartup + SlugCheckDelay;

			if (!_slugChecking)
			{
				EditorApplication.update += PollSlugCheck;
			}
		}

		private void PollSlugCheck()
		{
			if (_pendingSlugCheck == null)
			{
				EditorApplication.update -= PollSlugCheck;
				return;
			}

			if (EditorApplication.timeSinceStartup < _slugCheckScheduledTime) return;

			var slug = _pendingSlugCheck;
			_pendingSlugCheck = null;
			_slugChecking = true;
			_slugAvailabilityLabel.text = "...";
			_slugAvailabilityLabel.RemoveFromClassList("slug-available");
			_slugAvailabilityLabel.RemoveFromClassList("slug-taken");

			PkgLnkApiClient.CheckSlugAvailability(slug, (available, error) =>
			{
				_slugChecking = false;

				if (error != null)
				{
					_slugAvailabilityLabel.text = string.Empty;
					return;
				}

				_slugAvailable = available;
				_slugAvailabilityLabel.text = available ? "Available" : "Taken";
				_slugAvailabilityLabel.RemoveFromClassList(available ? "slug-taken" : "slug-available");
				_slugAvailabilityLabel.AddToClassList(available ? "slug-available" : "slug-taken");

				if (_pendingSlugCheck != null)
				{
					EditorApplication.update += PollSlugCheck;
				}
				else
				{
					EditorApplication.update -= PollSlugCheck;
				}
			});
		}

		private void OnSubmit()
		{
			_errorLabel.style.display = DisplayStyle.None;

			var name = _nameField.value?.Trim() ?? string.Empty;
			var slug = _slugField.value?.Trim() ?? string.Empty;
			var description = _descriptionField.value?.Trim() ?? string.Empty;

			if (string.IsNullOrEmpty(name))
			{
				ShowError("Name is required.");
				return;
			}

			if (!_isEditMode)
			{
				if (slug.Length < 3)
				{
					ShowError("Slug must be at least 3 characters.");
					return;
				}

				if (!_slugAvailable)
				{
					ShowError("Slug is not available.");
					return;
				}
			}

			_formSection.style.display = DisplayStyle.None;
			_savingSection.style.display = DisplayStyle.Flex;

			if (_isEditMode)
			{
				PkgLnkApiClient.UpdateCollection(PkgLnkAuth.Token, _existing.slug, name, description, (response, error) =>
				{
					OnApiResponse(response?.collection, error);
				});
			}
			else
			{
				PkgLnkApiClient.CreateCollection(PkgLnkAuth.Token, slug, name, description, (response, error) =>
				{
					OnApiResponse(response?.collection, error);
				});
			}
		}

		private void OnApiResponse(CollectionData collection, string error)
		{
			if (error != null)
			{
				_savingSection.style.display = DisplayStyle.None;
				_formSection.style.display = DisplayStyle.Flex;

				// Check for scope/auth issues
				if (error.StartsWith("403"))
				{
					ShowError("Token needs refreshed permissions. Please sign out and sign in again.");
				}
				else
				{
					// Strip the HTTP status code prefix for cleaner display
					var colonIdx = error.IndexOf(": ", StringComparison.Ordinal);
					ShowError(colonIdx > 0 ? error.Substring(colonIdx + 2) : error);
				}
				return;
			}

			_onCompleted?.Invoke(collection);
			Close();
		}

		private void ShowError(string message)
		{
			_errorLabel.text = message;
			_errorLabel.style.display = DisplayStyle.Flex;
		}

		private static string GenerateSlug(string input)
		{
			if (string.IsNullOrEmpty(input)) return string.Empty;

			var slug = input.ToLowerInvariant();
			var sb = new System.Text.StringBuilder(slug.Length);

			foreach (var c in slug)
			{
				if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
					sb.Append(c);
				else if (c == ' ' || c == '_' || c == '-')
					sb.Append('-');
			}

			// Trim consecutive hyphens
			var result = sb.ToString();
			while (result.Contains("--"))
			{
				result = result.Replace("--", "-");
			}

			return result.Trim('-');
		}

		private void OnDestroy()
		{
			EditorApplication.update -= PollSlugCheck;
		}
	}
}
