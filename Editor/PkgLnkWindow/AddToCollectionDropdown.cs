using System;
using System.Collections.Generic;
using Nonatomic.PkgLnk.Editor.Api;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nonatomic.PkgLnk.Editor.PkgLnkWindow
{
	/// <summary>
	/// Floating dropdown for adding a package to one of the user's collections.
	/// </summary>
	public class AddToCollectionDropdown : VisualElement
	{
		private readonly Action _onClose;
		private readonly Action<CollectionData> _onCreateNew;

		private readonly Label _titleLabel;
		private readonly VisualElement _listContainer;
		private readonly Label _loadingLabel;
		private readonly Label _emptyLabel;
		private readonly Button _createButton;
		private readonly Button _closeButton;

		private PackageData _targetPackage;
		private readonly HashSet<string> _addedCollections = new HashSet<string>();

		public AddToCollectionDropdown(Action onClose, Action<CollectionData> onCreateNew)
		{
			_onClose = onClose;
			_onCreateNew = onCreateNew;

			AddToClassList("add-to-collection-dropdown");
			style.display = DisplayStyle.None;

			// Header
			var header = new VisualElement();
			header.AddToClassList("add-to-collection-header");
			Add(header);

			_titleLabel = new Label("Add to Collection");
			_titleLabel.AddToClassList("add-to-collection-title");
			header.Add(_titleLabel);

			_closeButton = new Button(() => Hide()) { text = "\u00d7" };
			_closeButton.AddToClassList("add-to-collection-close");
			header.Add(_closeButton);

			// List
			_listContainer = new VisualElement();
			_listContainer.AddToClassList("add-to-collection-list");
			Add(_listContainer);

			// Loading
			_loadingLabel = new Label("Loading...");
			_loadingLabel.AddToClassList("add-to-collection-loading");
			_loadingLabel.style.display = DisplayStyle.None;
			Add(_loadingLabel);

			// Empty state
			_emptyLabel = new Label("No collections yet.");
			_emptyLabel.AddToClassList("add-to-collection-empty");
			_emptyLabel.style.display = DisplayStyle.None;
			Add(_emptyLabel);

			// Create new button
			_createButton = new Button(OnCreateNewClicked) { text = "+ Create New" };
			_createButton.AddToClassList("add-to-collection-create");
			Add(_createButton);
		}

		/// <summary>Shows the dropdown for a specific package.</summary>
		public void Show(PackageData package)
		{
			_targetPackage = package;
			_addedCollections.Clear();
			_listContainer.Clear();
			style.display = DisplayStyle.Flex;

			_loadingLabel.style.display = DisplayStyle.Flex;
			_emptyLabel.style.display = DisplayStyle.None;
			_listContainer.style.display = DisplayStyle.None;

			PkgLnkApiClient.FetchMyCollections(PkgLnkAuth.Token, OnCollectionsLoaded);
		}

		/// <summary>Hides the dropdown.</summary>
		public void Hide()
		{
			style.display = DisplayStyle.None;
			_onClose?.Invoke();
		}

		private void OnCollectionsLoaded(CollectionsResponse response, string error)
		{
			_loadingLabel.style.display = DisplayStyle.None;

			if (error != null || response == null || response.collections == null || response.collections.Length == 0)
			{
				_emptyLabel.style.display = DisplayStyle.Flex;
				return;
			}

			_listContainer.style.display = DisplayStyle.Flex;

			foreach (var collection in response.collections)
			{
				var row = BuildCollectionRow(collection);
				_listContainer.Add(row);
			}
		}

		private VisualElement BuildCollectionRow(CollectionData collection)
		{
			var row = new VisualElement();
			row.AddToClassList("add-to-collection-row");

			var info = new VisualElement();
			info.AddToClassList("add-to-collection-info");
			row.Add(info);

			var nameLabel = new Label(collection.name);
			nameLabel.AddToClassList("add-to-collection-name");
			info.Add(nameLabel);

			var countLabel = new Label($"{collection.package_count} packages");
			countLabel.AddToClassList("add-to-collection-count");
			info.Add(countLabel);

			var addButton = new Button { text = "Add" };
			addButton.AddToClassList("add-to-collection-add-btn");

			var capturedCollection = collection;
			addButton.clicked += () => OnAddClicked(capturedCollection, addButton);
			row.Add(addButton);

			return row;
		}

		private void OnAddClicked(CollectionData collection, Button button)
		{
			if (_targetPackage == null) return;

			button.text = "Adding...";
			button.SetEnabled(false);

			PkgLnkApiClient.AddPackageToCollection(
				PkgLnkAuth.Token,
				collection.slug,
				_targetPackage.id,
				(response, error) =>
				{
					if (error != null)
					{
						if (error.Contains("409") || error.Contains("already"))
						{
							button.text = "Already added";
						}
						else if (error.StartsWith("403"))
						{
							button.text = "No permission";
						}
						else
						{
							button.text = "Error";
							button.SetEnabled(true);
						}
						return;
					}

					button.text = "Added";
					_addedCollections.Add(collection.slug);
				});
		}

		private void OnCreateNewClicked()
		{
			CollectionFormWindow.ShowCreate(newCollection =>
			{
				_onCreateNew?.Invoke(newCollection);

				// Refresh the list and re-show
				if (_targetPackage != null)
				{
					Show(_targetPackage);
				}
			});
		}
	}
}
