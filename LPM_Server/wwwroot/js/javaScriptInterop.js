// Pre-define drop handler so ondrop attributes don't error before setup
window._lpmDrop = function() { console.log('[Drop] _lpmDrop called before setup — ignored'); };
window._lpmDropRef = null;

window.interop = {
  getElement: function (elementRef) {
    // Function to get the CSS selector for the given element reference
    return new Promise((resolve, reject) => {
      try {
        const selector = document.querySelector(elementRef);
        if (selector) {
          resolve(true);
        } else {
          reject(false);
        }
      } catch (error) {
        reject(error);
      }
    });
  },
  isEleExist: function (elementRef) {
    // Function to get the CSS selector for the given element reference
    const selector = document.querySelector(elementRef);
    if (selector) {
      return true;
    } else {
      return false;
    }
  },
  getBoundry: function (elementRef) {
    const rect = elementRef?.getBoundingClientRect();
    return rect;
  },
  inner: function (arg) {
    if (arg == "innerWidth") {
      return window.innerWidth ?? 992;
    }
    if (arg == "innerHeight") {
      return window.innerHeight ?? 992;
    }
    return 0
  },
  MenuNavElement: function (elementRef) {
    // Function to get the width of the given element reference
    return new Promise((resolve, reject) => {
      try {
        const element = document.querySelector(elementRef);
        if (element) {
          const scrollWidth = element.scrollWidth; // Get the scrollWidth of the element
          const marginInlineStart = Math.ceil(
            Number(
              window.getComputedStyle(element).marginInlineStart.split("px")[0]
            )
          ); // Get the scrollWidth of the element
          resolve({ scrollWidth, marginInlineStart }); // Return both element and width
        } else {
          reject("Element not found");
        }
      } catch (error) {
        reject(error);
      }
    });
  },
  MenuNavmarginInlineStart: function (selector, value) {
    // Function to get the width of the given element reference
    return new Promise((resolve, reject) => {
      try {
        const element = document.querySelector(selector);
        if (element) {
          element.style.marginInlineStart = value;
          resolve(element); // Return both element and width
        } else {
          reject("Element not found");
        }
      } catch (error) {
        reject(error);
      }
    });
  },
  mainSidebarOffset: function (elementRef) {
    // Function to get the width of the given element reference
    return new Promise((resolve, reject) => {
      try {
        const element = document.querySelector(elementRef);
        if (element) {
          const mainSidebarOffset = element.offsetWidth; // Get the scrollWidth of the element
          resolve(mainSidebarOffset); // Return both element and width
        } else {
          reject("Element not found");
        }
      } catch (error) {
        reject(error);
      }
    });
  },
  addClass: function (elementRef, className) {
    const element = document.querySelector(elementRef);
    if (element) {
      element.classList.add(className);
    }
  },
  removeClass: function (elementRef, className) {
    const element = document.querySelector(elementRef);
    if (element) {
      element.classList.remove(className);
    }
  },
  addClassToHtml: (className) => {
    document.documentElement.classList.add(className);
  },
  setclearCssVariables: function () {
    document.documentElement.style = "";
  },
  setCssVariable: function (variableName, value) {
    document.documentElement.style.setProperty(variableName, value);
  },
  removeCssVariable: function (variableName, value) {
    document.documentElement.style.removeProperty(variableName, value);
  },
  setCustomCssVariable: function (element, variableName, value) {
    let ele = document.querySelector(element);
    if (ele) {
      ele.style.setProperty(variableName, value);
    }
  },
  removeClassFromHtml: (className) => {
    document.documentElement.classList.remove(className);
  },
  getAttributeToHtml: (attributeName) => {
    return document.documentElement.getAttribute(attributeName);
  },
  addAttributeToHtml: (attributeName, attributeValue) => {
    document.documentElement.setAttribute(attributeName, attributeValue);
  },
  removeAttributeFromHtml: (attributeName) => {
    document.documentElement.removeAttribute(attributeName);
  },
  getAttribute: function (elementRef, attributeName) {
    return new Promise((resolve, reject) => {
      try {
        const selector = document.querySelector(elementRef);
        if (selector) {
          resolve(selector.getAttribute(attributeName));
        } else {
          reject("Element not found");
        }
      } catch (error) {
        reject(error);
      }
    });
  },
  setAttribute: function (elementRef, attributeName, attributeValue) {
    return new Promise((resolve, reject) => {
      try {
        const selector = document.querySelector(elementRef);
        if (selector) {
          resolve(selector.setAttribute(attributeName, attributeValue));
        } else {
          reject("Element not found");
        }
      } catch (error) {
        reject(error);
      }
    });
  },

  setLocalStorageItem: function (key, value) {
    localStorage.setItem(key, value);
  },
  removeLocalStorageItem: function (key) {
    localStorage.removeItem(key);
  },
  getAllLocalStorageItem: function () {
     return localStorage;
  },
  getLocalStorageItem: function (key) {
    return localStorage.getItem(key);
  },
  clearAllLocalStorage: function () {
    localStorage.clear();
  },
  directionChange: function (dataId) {
    let element = document.querySelector(`[data-id="${dataId}"]`);
    let html = document.documentElement;
    if (element) {
      const listItem = element.closest("li");
      if (listItem) {
        // Find the first sibling <ul> element
        const siblingUL = listItem.querySelector("ul");
        let outterUlWidth = 0;
        let listItemUL = listItem.closest("ul:not(.main-menu)");
        while (listItemUL) {
          listItemUL = listItemUL.parentElement.closest("ul:not(.main-menu)");
          if (listItemUL) {
            outterUlWidth += listItemUL.clientWidth;
          }
        }
        if (siblingUL) {
          // You've found the sibling <ul> element
          let siblingULRect = listItem.getBoundingClientRect();
          if (html.getAttribute("dir") == "rtl") {
            if (
              siblingULRect.left - siblingULRect.width - outterUlWidth + 150 <
                0 &&
              outterUlWidth < window.innerWidth &&
              outterUlWidth + siblingULRect.width + siblingULRect.width <
                window.innerWidth
            ) {
              return true;
            } else {
              return false;
            }
          } else {
            if (
              outterUlWidth + siblingULRect.right + siblingULRect.width + 50 >
                window.innerWidth &&
              siblingULRect.right >= 0 &&
              outterUlWidth + siblingULRect.width + siblingULRect.width <
                window.innerWidth
            ) {
              return true;
            } else {
              return false;
            }
          }
        }
      }
    }
    return false;
  },
  groupDirChange: function () {
    let elemList = {
      added: [],
      removed: [],
      clearNavDropdown: false,
    };
    if (
      document.querySelector("html").getAttribute("data-nav-layout") ===
        "horizontal" &&
      window.innerWidth > 992
    ) {
      let activeMenus = document.querySelectorAll(".slide.has-sub.open > ul");
      activeMenus.forEach((e) => {
        let target = e;
        let html = document.documentElement;

        const listItem = target.closest("li");
        // Get the position of the clicked element
        var dropdownRect = listItem.getBoundingClientRect();
        var dropdownWidth = target.getBoundingClientRect().width;

        // Calculate the right edge position
        var rightEdge = dropdownRect.right + dropdownWidth;
        var leftEdge = dropdownRect.left - dropdownWidth;

        if (html.getAttribute("dir") == "rtl") {
          // Check if moving out to the right
          if (e.classList.contains("child1")) {
            if (dropdownRect.left < 0) {
              elemList.clearNavDropdown = true;
            }
          }
          if (leftEdge < 0) {
            elemList.added.push(
              target.previousElementSibling.getAttribute("data-id")
            );
          } else {
            if (
              listItem.closest("ul").classList.contains("force-left") &&
              rightEdge < window.innerWidth
            ) {
              elemList.added.push(
                target.previousElementSibling.getAttribute("data-id")
              );
            } else {
              // Reset classes and position if not moving out
              elemList.removed.push(
                target.previousElementSibling.getAttribute("data-id")
              );
            }
          }
        } else {
          // Check if moving out to the right
          if (e.classList.contains("child1")) {
            if (dropdownRect.right > window.innerWidth) {
              elemList.clearNavDropdown = true;
            }
          }
          if (rightEdge > window.innerWidth) {
            elemList.added.push(
              target.previousElementSibling.getAttribute("data-id")
            );
          } else {
            if (
              listItem.closest("ul").classList.contains("force-left") &&
              leftEdge > 0
            ) {
              elemList.added.push(
                target.previousElementSibling.getAttribute("data-id")
              );
            }
            // Check if moving out to the left
            else if (leftEdge < 0) {
              elemList.removed.push(
                target.previousElementSibling.getAttribute("data-id")
              );
            } else {
              elemList.removed.push(
                target.previousElementSibling.getAttribute("data-id")
              );
            }
          }
        }
      });
      let leftForceItem = document.querySelector(
        ".slide-menu.active.force-left"
      );
      if (leftForceItem) {
        if (document.querySelector("html").getAttribute("dir") != "rtl") {
          let check = leftForceItem.getBoundingClientRect().right;
          if (check < innerWidth) {
            elemList.removed.push(
              leftForceItem.previousElementSibling.getAttribute("data-id")
            );
          } else if (leftForceItem.getBoundingClientRect().left < 0) {
            if (
              document.documentElement.getAttribute("data-nav-style") ==
                "menu-hover" ||
              document.documentElement.getAttribute("data-nav-style") ==
                "icon-hover" ||
              window.innerWidth > 992
            ) {
              elemList.removed.push(
                leftForceItem.previousElementSibling.getAttribute("data-id")
              );
            }
          }
        } else {
          let check =
            leftForceItem.getBoundingClientRect().left -
            leftForceItem.parentElement.closest(".slide-menu")?.clientWidth -
            leftForceItem.getBoundingClientRect().width;
          if (check > 0) {
            if (
              document.documentElement.getAttribute("data-nav-style") ==
                "menu-hover" ||
              document.documentElement.getAttribute("data-nav-style") ==
                "icon-hover" ||
              window.innerWidth > 992
            ) {
              elemList.removed.push(
                leftForceItem.previousElementSibling.getAttribute("data-id")
              );
            }
          }
        }
      }

      let elements = document.querySelectorAll(".main-menu .has-sub ul");
      elements.forEach((e) => {
        if (isElementVisible(e)) {
          let ele = e.getBoundingClientRect();
          if (document.documentElement.getAttribute("dir") == "rtl") {
            if (ele.left < 0) {
              if (e.classList.contains("child1")) {
                elemList.removed.push(
                  e.previousElementSibling.getAttribute("data-id")
                );
              } else {
                elemList.added.push(
                  e.previousElementSibling.getAttribute("data-id")
                );
              }
            }
          } else {
            if (ele.right > innerWidth) {
              if (e.classList.contains("child1")) {
                elemList.removed.push(
                  e.previousElementSibling.getAttribute("data-id")
                );
              } else {
                elemList.added.push(
                  e.previousElementSibling.getAttribute("data-id")
                );
              }
            }
          }
        }
      });
    }

    elemList.added = [...new Set(elemList.added)];
    elemList.removed = [...new Set(elemList.removed)];
    return elemList;
  },
  updateScrollVisibility : function(dotnetHelper) {
    window.onscroll = function() {
        var scrollHeight = window.scrollY;
        dotnetHelper.invokeMethodAsync('UpdateScrollVisibility', scrollHeight);
    }
  },
  scrollToTop : function() {
    window.scrollTo({
        top: 0,
        behavior: "smooth"
    });
  },
  _scrollHandler: null,
  _headerScrollHandler: null,
  registerScrollListener: function (dotnetHelper) {
      interop._scrollHandler = function () {
          var scrollY = window.scrollY || window.pageYOffset;
          dotnetHelper.invokeMethodAsync("SetStickyClass", scrollY);
      };
      window.addEventListener('scroll', interop._scrollHandler);

    // Trigger initial check
    var scrollY = window.scrollY || window.pageYOffset;
    dotnetHelper.invokeMethodAsync("SetStickyClass", scrollY);
  },
  unregisterScrollListener: function () {
      if (interop._scrollHandler) {
          window.removeEventListener('scroll', interop._scrollHandler);
          interop._scrollHandler = null;
      }
  },
  registerheaderScrollListener: function (dotnetHelper) {
      interop._headerScrollHandler = function () {
          var scrollY = window.scrollY || window.pageYOffset;
          dotnetHelper.invokeMethodAsync("SetStickyClass1", scrollY);
      };
      window.addEventListener('scroll', interop._headerScrollHandler);

    // Trigger initial check
    var scrollY = window.scrollY || window.pageYOffset;
    dotnetHelper.invokeMethodAsync("SetStickyClass1", scrollY);
  },
  unregisterheaderScrollListener: function () {
      if (interop._headerScrollHandler) {
          window.removeEventListener('scroll', interop._headerScrollHandler);
          interop._headerScrollHandler = null;
      }
  },
  initializeTooltips: function() {
    const tooltipTriggerList = document.querySelectorAll('[data-bs-toggle="tooltip"]');
    const tooltipList = [...tooltipTriggerList].map((tooltipTriggerEl) => new bootstrap.Tooltip(tooltipTriggerEl));
  },
  initializePopover: function() {
    const popoverTriggerList = document.querySelectorAll('[data-bs-toggle="popover"]');
    const popoverList = [...popoverTriggerList].map((popoverTriggerEl) => new bootstrap.Popover(popoverTriggerEl));
  },
  initCardRemove: function () {
    let DIV_CARD = ".card";
    let cardRemoveBtn = document.querySelectorAll('[data-bs-toggle="card-remove"]');
    cardRemoveBtn.forEach((ele) => {
        ele.addEventListener("click", function (e) {
            e.preventDefault();
            let $this = this;
            let card = $this.closest(DIV_CARD);
            card.remove();
            return false;
        });
      });
  },
  initCardFullscreen: function () {
      let DIV_CARD = ".card";
      let cardFullscreenBtn = document.querySelectorAll('[data-bs-toggle="card-fullscreen"]');
      cardFullscreenBtn.forEach((ele) => {
          ele.addEventListener("click", function (e) {
              let $this = this;
              let card = $this.closest(DIV_CARD);
              card.classList.toggle("card-fullscreen");
              card.classList.remove("card-collapsed");
              e.preventDefault();
              return false;
          });
      });
  },
  InitCarousel: function () {
    const myCarouselElements = document.querySelectorAll('.carousel');
  
    myCarouselElements.forEach(function (carouselElement) {
      new bootstrap.Carousel(carouselElement, {
        interval: 2000,
        touch: false
      });
    });
  },

  // ── ARF table arrow key navigation ──
  setupArfNavigation: function () {
      if (document._arfNavSetup) return;
      document._arfNavSetup = true;

      function focusCell(id, cursorAtEnd) {
          var el = document.getElementById(id);
          if (!el) return;
          el.focus();
          setTimeout(function () {
              var pos = cursorAtEnd ? el.value.length : 0;
              try { el.setSelectionRange(pos, pos); } catch (_) {}
          }, 0);
      }

      function atEnd(el) {
          return el.selectionStart === el.value.length && el.selectionEnd === el.value.length;
      }
      function atStart(el) {
          return el.selectionStart === 0 && el.selectionEnd === 0;
      }
      function hasNewlines(el) {
          return (el.value || '').indexOf('\n') >= 0;
      }

      document.addEventListener('keydown', function (e) {
          var el = e.target;
          if (!el || !el.id) return;
          var m = el.id.match(/^arf-c-(\d+)-(\d+)-(\d+)$/);
          if (!m) return;

          var tbl = parseInt(m[1]), row = parseInt(m[2]), col = parseInt(m[3]);
          var maxCol = 3;
          var key = e.key;

          if (key === 'ArrowUp') {
              if (hasNewlines(el)) return;
              if (row > 0) { e.preventDefault(); focusCell('arf-c-' + tbl + '-' + (row - 1) + '-' + col, true); }
          }
          else if (key === 'ArrowDown') {
              if (hasNewlines(el)) return;
              e.preventDefault();
              focusCell('arf-c-' + tbl + '-' + (row + 1) + '-' + col, true);
          }
          else if (key === 'ArrowRight' && atEnd(el)) {
              if (col < maxCol) {
                  e.preventDefault();
                  focusCell('arf-c-' + tbl + '-' + row + '-' + (col + 1), false);
              } else {
                  // Wrap to next row, col 0 (same table)
                  e.preventDefault();
                  focusCell('arf-c-' + tbl + '-' + (row + 1) + '-0', false);
              }
          }
          else if (key === 'ArrowLeft' && atStart(el)) {
              if (col > 0) {
                  e.preventDefault();
                  focusCell('arf-c-' + tbl + '-' + row + '-' + (col - 1), true);
              } else if (row > 0) {
                  // Wrap to prev row, last col (same table)
                  e.preventDefault();
                  focusCell('arf-c-' + tbl + '-' + (row - 1) + '-' + maxCol, true);
              }
          }
      });
  },

  // ── Drag-and-drop file handler for Add Session wizard ──
  setupFileDrop: function (dotNetRef) {
      console.log('[Drop] setupFileDrop called, dotNetRef=' + (dotNetRef ? 'OK' : 'NULL'));
      window._lpmDropRef = dotNetRef;
      window._lpmDrop = async function (e, mode) {
          console.log('[Drop] _lpmDrop fired, mode=' + mode + ', files=' + (e.dataTransfer.files ? e.dataTransfer.files.length : 0) + ', ref=' + (window._lpmDropRef ? 'OK' : 'NULL'));
          var files = e.dataTransfer.files;
          if (!files || files.length === 0) { console.warn('[Drop] No files in drop event'); return; }
          if (!window._lpmDropRef) { console.warn('[Drop] No dotNetRef'); return; }
          for (var i = 0; i < files.length; i++) {
              var f = files[i];
              console.log('[Drop] Processing file: ' + f.name + ' (' + f.size + ' bytes, type=' + f.type + ')');
              try {
                  var buf = await f.arrayBuffer();
                  console.log('[Drop] ArrayBuffer read: ' + buf.byteLength + ' bytes');
                  // Convert to base64 in chunks to avoid call stack overflow
                  var bytes = new Uint8Array(buf);
                  var binary = '';
                  var chunkSize = 8192;
                  for (var j = 0; j < bytes.length; j += chunkSize) {
                      binary += String.fromCharCode.apply(null, bytes.subarray(j, j + chunkSize));
                  }
                  var base64 = btoa(binary);
                  console.log('[Drop] Base64 length: ' + base64.length + ', calling Blazor...');
                  await window._lpmDropRef.invokeMethodAsync('OnFileDrop', mode, f.name, base64);
                  console.log('[Drop] Blazor call completed for: ' + f.name);
              } catch (err) {
                  console.error('[Drop] Failed to process file:', f.name, err);
              }
          }
      };
  },

};

function isElementVisible(element) {
  const computedStyle = window.getComputedStyle(element);
  return computedStyle.display != "none";
}

window.scrollToEdge = (elementId, dir) => {
    const el = document.getElementById(elementId);
    if (!el) return;

    const left = (dir === "left") ? 0 : el.scrollWidth;
    el.scrollTo({ left, behavior: "smooth" });
};

// ── Resizable table columns ──────────────────────────────────────────────────
// Call with the table element's id. Adds drag handles to all <th> elements.
// Each column is sized independently; the table expands instead of squeezing others.
window.lpmInitColResize = function (tableId) {
    const table = document.getElementById(tableId);
    if (!table) return;

    // Switch to fixed layout so column widths are respected
    table.style.tableLayout = 'fixed';

    // Snapshot initial pixel widths from rendered layout, then lock them in
    const ths = Array.from(table.querySelectorAll('thead th'));
    ths.forEach(th => {
        th.style.width = th.offsetWidth + 'px';
        th.style.overflow = 'hidden';
        th.style.position = 'relative';
        th.style.boxSizing = 'border-box';
    });

    // Let the table grow freely (don't constrain to 100%)
    table.style.width = 'auto';
    table.style.minWidth = '100%';

    ths.forEach(th => {
        const handle = document.createElement('span');
        handle.style.cssText =
            'position:absolute;right:0;top:0;bottom:0;width:6px;' +
            'cursor:col-resize;z-index:10;user-select:none;' +
            'background:transparent;';
        handle.addEventListener('mouseenter', () => handle.style.background = 'rgba(99,102,241,.35)');
        handle.addEventListener('mouseleave', () => { if (!handle._dragging) handle.style.background = 'transparent'; });

        handle.addEventListener('mousedown', e => {
            e.preventDefault();
            e.stopPropagation();
            handle._dragging = true;
            const startX = e.pageX;
            const startW = th.offsetWidth;

            const onMove = e => {
                const newW = Math.max(40, startW + (e.pageX - startX));
                th.style.width = newW + 'px';
            };
            const onUp = () => {
                handle._dragging = false;
                handle.style.background = 'transparent';
                document.removeEventListener('mousemove', onMove);
                document.removeEventListener('mouseup', onUp);
            };
            document.addEventListener('mousemove', onMove);
            document.addEventListener('mouseup', onUp);
        });

        th.appendChild(handle);
    });
};

// Download trigger — url and filename passed as parameters, never concatenated into JS
window.lpmTriggerDownload = function (url, filename) {
    var a = document.createElement('a');
    a.href = url;
    if (filename) a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
};

// Click a hidden input/button by element ID — id is a C# constant, never user input
window.lpmClickById = function (id) {
    var el = document.getElementById(id);
    if (el) el.click();
};

// Navigate to a URL — used when NavManager.NavigateTo is unreliable (e.g. PcFolder layout)
window.lpmNavigateTo = function (url) {
    window.location.href = url;
};

// Backup authentication — password passed as a typed argument, never concatenated into JS
window.lpmBackupAuth = async function (password) {
    var fd = new FormData();
    fd.append('password', password);
    var resp = await fetch('/api/backup-auth', { method: 'POST', body: fd });
    var json = await resp.json();
    if (json.locked) return 'LOCKED';
    if (!json.ok) return 'FAIL:' + json.remaining;
    return json.token;
};
