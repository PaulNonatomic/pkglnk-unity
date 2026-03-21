using System;
using System.Collections.Generic;
using Nonatomic.PkgLnk.Editor.Api;
using Nonatomic.PkgLnk.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Full-screen detail view for a collection. Shows header, package list
	/// with per-package status indicators, and batch-install controls.
	/// </summary>
	public class CollectionDetailView : VisualElement
	{
		private readonly Action _onBack;

		// Header
		private readonly VisualElement _ownerAvatar;
		private readonly Label _ownerLabel;
		private readonly Label _nameLabel;
		private readonly Label _descLabel;
		private readonly Label _packageCountLabel;
		private readonly VisualElement _tagsRow;

		// Install all
		private readonly Button _installAllButton;
		private readonly Button _cancelButton;

		// Package list
		private readonly ScrollView _packageScroll;
		private readonly VisualElement _packageListContainer;

		// Status / error
		private readonly Label _statusLabel;
		private readonly Label _errorLabel;

		private CollectionData _collection;
		private PackageData[] _packages;
		private readonly List<PackageRow> _packageRows = new List<PackageRow>();
		private string _boundAvatarUrl;

		public CollectionDetailView(Action onBack)
		{
			_onBack = onBack;

			AddToClassList("collection-detail");
			style.display = DisplayStyle.None;

			// Back button
			var backButton = new Button(() => _onBack?.Invoke());
			backButton.text = "\u2190 Back";
			backButton.AddToClassList("detail-back-button");
			Add(backButton);

			// Collection name
			_nameLabel = new Label();
			_nameLabel.AddToClassList("detail-title");
			Add(_nameLabel);

			// Owner row
			var ownerRow = new VisualElement();
			ownerRow.AddToClassList("collection-detail-owner-row");
			Add(ownerRow);

			_ownerAvatar = new VisualElement();
			_ownerAvatar.AddToClassList("owner-avatar");
			ownerRow.Add(_ownerAvatar);

			_ownerLabel = new Label();
			_ownerLabel.AddToClassList("detail-owner");
			ownerRow.Add(_ownerLabel);

			_packageCountLabel = new Label();
			_packageCountLabel.AddToClassList("collection-detail-count");
			ownerRow.Add(_packageCountLabel);

			// Description
			_descLabel = new Label();
			_descLabel.AddToClassList("detail-description");
			Add(_descLabel);

			// Tags
			_tagsRow = new VisualElement();
			_tagsRow.AddToClassList("topic-tags-row");
			_tagsRow.style.display = DisplayStyle.None;
			Add(_tagsRow);

			// Install All / Cancel row
			var actionRow = new VisualElement();
			actionRow.AddToClassList("collection-action-row");
			Add(actionRow);

			_installAllButton = new Button(OnInstallAllClicked);
			_installAllButton.AddToClassList("install-all-button");
			actionRow.Add(_installAllButton);

			_cancelButton = new Button(OnCancelClicked);
			_cancelButton.text = "Cancel";
			_cancelButton.AddToClassList("batch-cancel-button");
			_cancelButton.style.display = DisplayStyle.None;
			actionRow.Add(_cancelButton);

			// Separator
			var separator = new VisualElement();
			separator.AddToClassList("collection-detail-separator");
			Add(separator);

			// Status label (loading / error)
			_statusLabel = new Label();
			_statusLabel.AddToClassList("status-label");
			_statusLabel.style.display = DisplayStyle.None;
			Add(_statusLabel);

			_errorLabel = new Label();
			_errorLabel.AddToClassList("error-label");
			_errorLabel.style.display = DisplayStyle.None;
			Add(_errorLabel);

			// Package scroll view
			_packageScroll = new ScrollView(ScrollViewMode.Vertical);
			_packageScroll.AddToClassList("collection-package-scroll");
			Add(_packageScroll);

			_packageListContainer = _packageScroll.contentContainer;
		}

		/// <summary>
		/// Shows a loading state while the collection detail is being fetched.
		/// </summary>
		public void ShowLoading()
		{
			style.display = DisplayStyle.Flex;
			_nameLabel.text = string.Empty;
			_ownerLabel.text = string.Empty;
			_descLabel.text = string.Empty;
			_packageCountLabel.text = string.Empty;
			_tagsRow.style.display = DisplayStyle.None;
			_installAllButton.style.display = DisplayStyle.None;
			_cancelButton.style.display = DisplayStyle.None;
			_packageScroll.style.display = DisplayStyle.None;
			_errorLabel.style.display = DisplayStyle.None;
			_statusLabel.text = "Loading collection...";
			_statusLabel.style.display = DisplayStyle.Flex;
		}

		/// <summary>
		/// Shows an error state.
		/// </summary>
		public void ShowError(string error)
		{
			_statusLabel.style.display = DisplayStyle.None;
			_packageScroll.style.display = DisplayStyle.None;
			_errorLabel.text = error;
			_errorLabel.style.display = DisplayStyle.Flex;
		}

		/// <summary>
		/// Populates the detail view and immediately starts installing all packages.
		/// </summary>
		public void ShowAndInstallAll(CollectionData collection, PackageData[] packages)
		{
			Show(collection, packages);
			OnInstallAllClicked();
		}

		/// <summary>
		/// Populates the detail view with collection data and its packages.
		/// </summary>
		public void Show(CollectionData collection, PackageData[] packages)
		{
			style.display = DisplayStyle.Flex;
			_statusLabel.style.display = DisplayStyle.None;
			_errorLabel.style.display = DisplayStyle.None;
			_packageScroll.style.display = DisplayStyle.Flex;

			_collection = collection;
			_packages = packages;

			_nameLabel.text = collection.name;
			_ownerLabel.text = collection.owner_username;

			if (!string.IsNullOrEmpty(collection.description))
			{
				_descLabel.text = collection.description;
				_descLabel.style.display = DisplayStyle.Flex;
			}
			else
			{
				_descLabel.style.display = DisplayStyle.None;
			}

			// Owner avatar
			var avatarUrl = !string.IsNullOrEmpty(collection.owner_avatar_url)
				? collection.owner_avatar_url
				: !string.IsNullOrEmpty(collection.owner_username)
					? $"https://github.com/{collection.owner_username}.png?size=40"
					: string.Empty;

			if (avatarUrl != _boundAvatarUrl)
			{
				_boundAvatarUrl = avatarUrl;
				_ownerAvatar.style.backgroundImage = StyleKeyword.None;

				if (!string.IsNullOrEmpty(avatarUrl))
				{
					ImageLoader.Load(avatarUrl, texture =>
					{
						if (texture == null || panel == null) return;
						if (_collection != collection) return;
						_ownerAvatar.style.backgroundImage = new StyleBackground(texture);
					});
				}
			}

			// Tags
			_tagsRow.Clear();
			var tagCount = collection.tags?.Length ?? 0;

			if (tagCount > 0)
			{
				foreach (var tag in collection.tags)
				{
					var tagLabel = new Label(tag);
					tagLabel.AddToClassList("topic-tag");
					_tagsRow.Add(tagLabel);
				}
				_tagsRow.style.display = DisplayStyle.Flex;
			}
			else
			{
				_tagsRow.style.display = DisplayStyle.None;
			}

			// Ensure installed cache is fresh
			PackageInstaller.InvalidateInstalledCache();

			// Build package rows
			BuildPackageRows();

			// Update install all button and count
			RefreshInstalledState();
		}

		/// <summary>
		/// Re-checks installed state for all packages and updates UI accordingly.
		/// </summary>
		public void RefreshInstalledState()
		{
			if (_packages == null) return;

			PackageInstaller.InvalidateInstalledCache();

			var installedCount = 0;

			foreach (var row in _packageRows)
			{
				var installed = PackageInstaller.IsInstalled(row.Package);
				row.UpdateInstalledState(installed);
				if (installed) installedCount++;
			}

			var totalCount = _packages.Length;
			_packageCountLabel.text = totalCount == 1 ? "1 package" : $"{totalCount} packages";

			var installableCount = totalCount - installedCount;

			if (installableCount > 0 && !BatchInstaller.IsRunning)
			{
				_installAllButton.text = "Install All";
				_installAllButton.SetEnabled(true);
				_installAllButton.style.display = DisplayStyle.Flex;
				_cancelButton.style.display = DisplayStyle.None;
			}
			else if (BatchInstaller.IsRunning)
			{
				_installAllButton.style.display = DisplayStyle.None;
			}
			else
			{
				_installAllButton.text = "All Installed";
				_installAllButton.SetEnabled(false);
				_installAllButton.style.display = DisplayStyle.Flex;
				_cancelButton.style.display = DisplayStyle.None;
			}
		}

		/// <summary>
		/// Hides the view and resets state.
		/// </summary>
		public void Hide()
		{
			style.display = DisplayStyle.None;
			_collection = null;
			_packages = null;
			_boundAvatarUrl = null;
			_packageRows.Clear();
			_packageListContainer.Clear();
		}

		private void BuildPackageRows()
		{
			_packageRows.Clear();
			_packageListContainer.Clear();

			if (_packages == null) return;

			foreach (var pkg in _packages)
			{
				var row = new PackageRow(pkg, OnRowInstallClicked);
				_packageRows.Add(row);
				_packageListContainer.Add(row);
			}
		}

		private void OnRowInstallClicked(PackageRow row)
		{
			if (PackageInstaller.IsInstalling || BatchInstaller.IsRunning) return;

			row.SetInstalling();

			PackageInstaller.Install(row.Package, (success, error) =>
			{
				if (success)
				{
					PackageInstaller.InvalidateInstalledCache();
					row.UpdateInstalledState(true);
					RefreshInstalledState();
				}
				else
				{
					row.SetFailed();
					_errorLabel.text = $"Failed to install {row.Package.display_name}: {error}";
					_errorLabel.style.display = DisplayStyle.Flex;
				}
			}, phase => row.SetInstallPhase(phase));
		}

		private void OnInstallAllClicked()
		{
			if (_packages == null || BatchInstaller.IsRunning) return;

			var toInstall = new List<PackageData>();
			foreach (var pkg in _packages)
			{
				if (!PackageInstaller.IsInstalled(pkg))
				{
					toInstall.Add(pkg);
				}
			}

			if (toInstall.Count == 0) return;

			_installAllButton.style.display = DisplayStyle.None;
			_cancelButton.style.display = DisplayStyle.Flex;
			_cancelButton.SetEnabled(true);
			_errorLabel.style.display = DisplayStyle.None;

			// Mark all pending rows as queued
			foreach (var row in _packageRows)
			{
				if (!PackageInstaller.IsInstalled(row.Package))
				{
					row.SetQueued();
				}
			}

			BatchInstaller.InstallAll(toInstall, OnBatchProgress, OnBatchComplete, OnBatchInstallPhase);
		}

		private void OnBatchProgress(BatchProgress progress)
		{
			foreach (var row in _packageRows)
			{
				if (row.Package != progress.CurrentPackage) continue;

				switch (progress.Phase)
				{
					case BatchPhase.Installing:
						row.SetInstalling();
						break;

					case BatchPhase.Completed:
						PackageInstaller.InvalidateInstalledCache();
						row.UpdateInstalledState(true);
						break;

					case BatchPhase.Failed:
						row.SetFailed();
						break;
				}
				break;
			}
		}

		private void OnBatchInstallPhase(PackageData pkg, InstallPhase phase)
		{
			foreach (var row in _packageRows)
			{
				if (row.Package != pkg) continue;
				row.SetInstallPhase(phase);
				break;
			}
		}

		private void OnBatchComplete(BatchResult result)
		{
			_cancelButton.style.display = DisplayStyle.None;

			if (result.Errors.Count > 0)
			{
				_errorLabel.text = string.Join("\n", result.Errors);
				_errorLabel.style.display = DisplayStyle.Flex;
			}

			PackageInstaller.InvalidateInstalledCache();
			RefreshInstalledState();
		}

		private void OnCancelClicked()
		{
			BatchInstaller.Cancel();
			_cancelButton.SetEnabled(false);

			// Reset queued rows back to pending
			foreach (var row in _packageRows)
			{
				if (!PackageInstaller.IsInstalled(row.Package))
				{
					row.ResetState();
				}
			}
		}

		// ─── Package Row ────────────────────────────────────────────────

		/// <summary>
		/// A single row in the collection's package list with an animated progress bar.
		/// The bar creeps forward slowly and jumps to milestone percentages when the
		/// server reports phase changes (resolving 33%, downloading 66%, complete 100%).
		/// </summary>
		private class PackageRow : VisualElement
		{
			private static readonly char[] SpinnerChars = { '|', '/', '-', '\\' };
			private const float CreepRate = 1.5f;
			private const float MaxDeltaTime = 0.1f;
			private const double SpinnerInterval = 0.15;

			public PackageData Package { get; }

			private readonly Button _installButton;
			private readonly Label _statusLabel;
			private readonly Label _spinnerLabel;
			private readonly VisualElement _progressTrack;
			private readonly VisualElement _progressFill;
			private readonly Action<PackageRow> _onInstallClicked;

			private bool _isInstalled;
			private bool _isAnimating;
			private float _currentPercent;
			private float _targetPercent;
			private float _ceilPercent;
			private double _lastAnimTime;
			private double _lastSpinnerTime;
			private int _spinnerFrame;

			public PackageRow(PackageData pkg, Action<PackageRow> onInstallClicked)
			{
				Package = pkg;
				_onInstallClicked = onInstallClicked;

				AddToClassList("collection-package-row");

				// Top section: info + spinner + status + button
				var topRow = new VisualElement();
				topRow.AddToClassList("collection-package-top-row");
				Add(topRow);

				var infoContainer = new VisualElement();
				infoContainer.AddToClassList("collection-package-info");
				topRow.Add(infoContainer);

				var nameLabel = new Label(pkg.display_name);
				nameLabel.AddToClassList("collection-package-name");
				infoContainer.Add(nameLabel);

				if (!string.IsNullOrEmpty(pkg.description))
				{
					var descLabel = new Label(pkg.description);
					descLabel.AddToClassList("collection-package-desc");
					infoContainer.Add(descLabel);
				}

				_spinnerLabel = new Label();
				_spinnerLabel.AddToClassList("row-spinner");
				_spinnerLabel.style.display = DisplayStyle.None;
				topRow.Add(_spinnerLabel);

				_statusLabel = new Label();
				_statusLabel.AddToClassList("row-status-label");
				_statusLabel.style.display = DisplayStyle.None;
				topRow.Add(_statusLabel);

				_installButton = new Button(() => _onInstallClicked?.Invoke(this));
				_installButton.AddToClassList("install-button");
				topRow.Add(_installButton);

				// Progress bar — track with fill child
				_progressTrack = new VisualElement();
				_progressTrack.AddToClassList("row-progress-track");
				_progressTrack.style.display = DisplayStyle.None;
				Add(_progressTrack);

				_progressFill = new VisualElement();
				_progressFill.AddToClassList("row-progress-fill");
				_progressTrack.Add(_progressFill);

				RegisterCallback<DetachFromPanelEvent>(_ => StopAnimation());

				_isInstalled = PackageInstaller.IsInstalled(pkg);
				ApplyState();
			}

			public void UpdateInstalledState(bool installed)
			{
				_isInstalled = installed;
				ApplyState();
			}

			public void SetQueued()
			{
				_installButton.text = "Queued";
				_installButton.SetEnabled(false);
				_installButton.RemoveFromClassList("installed-button");
				_statusLabel.text = "Queued";
				_statusLabel.style.display = DisplayStyle.Flex;
				_statusLabel.RemoveFromClassList("row-status-error");
				_progressFill.RemoveFromClassList("row-progress-fill-failed");
				RemoveFromClassList("collection-package-row-installed");

				_currentPercent = 0;
				_targetPercent = 0;
				_ceilPercent = 0;
				_progressFill.style.width = new Length(0, LengthUnit.Percent);
				_progressTrack.style.display = DisplayStyle.Flex;
			}

			public void SetInstalling()
			{
				_installButton.text = "Installing...";
				_installButton.SetEnabled(false);
				_installButton.RemoveFromClassList("installed-button");
				_statusLabel.text = "Installing...";
				_statusLabel.style.display = DisplayStyle.Flex;
				_statusLabel.RemoveFromClassList("row-status-error");
				_progressFill.RemoveFromClassList("row-progress-fill-failed");
				RemoveFromClassList("collection-package-row-installed");

				_targetPercent = 5f;
				_ceilPercent = 28f;
				_progressTrack.style.display = DisplayStyle.Flex;
				StartAnimation();
			}

			public void SetFailed()
			{
				StopAnimation();
				_installButton.text = "Install";
				_installButton.SetEnabled(true);
				_installButton.RemoveFromClassList("installed-button");
				_statusLabel.text = "Failed";
				_statusLabel.style.display = DisplayStyle.Flex;
				_statusLabel.AddToClassList("row-status-error");
				_progressFill.AddToClassList("row-progress-fill-failed");
				RemoveFromClassList("collection-package-row-installed");
			}

			public void SetInstallPhase(InstallPhase phase)
			{
				switch (phase)
				{
					case InstallPhase.Resolving:
						_installButton.text = "Resolving...";
						_statusLabel.text = "Resolving...";
						_statusLabel.style.display = DisplayStyle.Flex;
						_targetPercent = 33f;
						_ceilPercent = 58f;
						break;

					case InstallPhase.Downloading:
						_installButton.text = "Downloading...";
						_statusLabel.text = "Downloading...";
						_statusLabel.style.display = DisplayStyle.Flex;
						_targetPercent = 66f;
						_ceilPercent = 85f;
						break;

					case InstallPhase.Importing:
						_installButton.text = "Importing...";
						_statusLabel.text = "Importing...";
						_statusLabel.style.display = DisplayStyle.Flex;
						_targetPercent = 90f;
						_ceilPercent = 98f;
						break;

					case InstallPhase.Complete:
						UpdateInstalledState(true);
						break;
				}
			}

			public void ResetState()
			{
				StopAnimation();
				_isInstalled = false;
				_currentPercent = 0;
				_progressFill.style.width = new Length(0, LengthUnit.Percent);
				_progressFill.RemoveFromClassList("row-progress-fill-failed");
				ApplyState();
			}

			private void ApplyState()
			{
				_statusLabel.RemoveFromClassList("row-status-error");
				_progressFill.RemoveFromClassList("row-progress-fill-failed");

				if (_isInstalled)
				{
					StopAnimation();
					_installButton.text = "Installed";
					_installButton.SetEnabled(false);
					_installButton.AddToClassList("installed-button");
					_statusLabel.style.display = DisplayStyle.None;
					AddToClassList("collection-package-row-installed");

					_currentPercent = 100f;
					_progressFill.style.width = new Length(100, LengthUnit.Percent);
					_progressTrack.style.display = DisplayStyle.Flex;
				}
				else
				{
					_installButton.text = "Install";
					_installButton.SetEnabled(true);
					_installButton.RemoveFromClassList("installed-button");
					_statusLabel.style.display = DisplayStyle.None;
					RemoveFromClassList("collection-package-row-installed");
					_progressTrack.style.display = DisplayStyle.None;
					_currentPercent = 0;
					_progressFill.style.width = new Length(0, LengthUnit.Percent);
				}
			}

			private void StartAnimation()
			{
				if (_isAnimating) return;
				_isAnimating = true;
				_lastAnimTime = EditorApplication.timeSinceStartup;
				_lastSpinnerTime = _lastAnimTime;
				_spinnerFrame = 0;
				_spinnerLabel.text = SpinnerChars[0].ToString();
				_spinnerLabel.style.display = DisplayStyle.Flex;
				EditorApplication.update += Animate;
			}

			private void StopAnimation()
			{
				if (!_isAnimating) return;
				_isAnimating = false;
				EditorApplication.update -= Animate;
				_spinnerLabel.style.display = DisplayStyle.None;
			}

			private void Animate()
			{
				var now = EditorApplication.timeSinceStartup;
				var dt = Mathf.Min((float)(now - _lastAnimTime), MaxDeltaTime);
				_lastAnimTime = now;

				// Creep progress toward ceiling
				if (_currentPercent < _ceilPercent)
				{
					_currentPercent += CreepRate * dt;
					if (_currentPercent > _ceilPercent) _currentPercent = _ceilPercent;

					// Jump to target if we haven't reached it yet
					if (_currentPercent < _targetPercent) _currentPercent = _targetPercent;

					_progressFill.style.width = new Length(_currentPercent, LengthUnit.Percent);
				}

				// Spinner
				if (now - _lastSpinnerTime >= SpinnerInterval)
				{
					_lastSpinnerTime = now;
					_spinnerFrame = (_spinnerFrame + 1) % SpinnerChars.Length;
					_spinnerLabel.text = SpinnerChars[_spinnerFrame].ToString();
				}
			}
		}
	}
}
