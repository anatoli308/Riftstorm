using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tolik.Riftstorm.Runtime.Core
{
    /// <summary>
    /// A fully stylable horizontal scroll view built from plain VisualElements.
    /// Every part (arrows, track, thumb) is a regular VisualElement that can be
    /// styled with USS background-image textures, unlike Unity's built-in ScrollView.
    /// </summary>
    [UxmlElement]
    internal partial class HorizontalScrollView : VisualElement
    {
        /// <summary>USS class applied to the root element.</summary>
        public static readonly string ussClassName = "horizontal-scroll-view";
        /// <summary>USS class applied to the viewport that clips content.</summary>
        public static readonly string viewportUssClassName = "horizontal-scroll-view__viewport";
        /// <summary>USS class applied to the content container.</summary>
        public static readonly string contentContainerUssClassName = "horizontal-scroll-view__content-container";
        /// <summary>USS class applied to the scrollbar row.</summary>
        public static readonly string scrollbarUssClassName = "horizontal-scroll-view__scrollbar";
        /// <summary>USS class applied to the left arrow button.</summary>
        public static readonly string arrowLeftUssClassName = "horizontal-scroll-view__arrow-left";
        /// <summary>USS class applied to the right arrow button.</summary>
        public static readonly string arrowRightUssClassName = "horizontal-scroll-view__arrow-right";
        /// <summary>USS class applied to the scrollbar track.</summary>
        public static readonly string trackUssClassName = "horizontal-scroll-view__track";
        /// <summary>USS class applied to the scrollbar thumb.</summary>
        public static readonly string thumbUssClassName = "horizontal-scroll-view__thumb";

        readonly VisualElement m_Viewport;
        readonly VisualElement m_ContentContainer;
        readonly VisualElement m_Scrollbar;
        readonly VisualElement m_ArrowLeft;
        readonly VisualElement m_ArrowRight;
        readonly VisualElement m_Track;
        readonly VisualElement m_Thumb;

        float m_ScrollOffset;
        float m_ContentWidth;
        float m_ViewportWidth;
        bool m_IsDraggingThumb;
        float m_ThumbDragStartX;
        float m_ThumbDragStartOffset;
        bool m_IsDraggingContent;
        bool m_ContentDragPending;
        int m_ContentDragPointerId;
        float m_ContentDragStartX;
        float m_ContentDragStartOffset;

        const float k_ScrollStep = 120f;
        const float k_WheelMultiplier = 40f;
        const float k_DragThreshold = 5f;

        /// <summary>The left arrow button element (stylable).</summary>
        public VisualElement ArrowLeft => m_ArrowLeft;

        /// <summary>The right arrow button element (stylable).</summary>
        public VisualElement ArrowRight => m_ArrowRight;

        /// <summary>The scrollbar track element (stylable).</summary>
        public VisualElement Track => m_Track;

        /// <summary>The scrollbar thumb element (stylable).</summary>
        public VisualElement Thumb => m_Thumb;

        /// <summary>The viewport element.</summary>
        public VisualElement Viewport => m_Viewport;

        /// <summary>Override so that children added via UXML or Add() go into the content container.</summary>
        public override VisualElement contentContainer => m_ContentContainer;

        /// <summary>
        /// Constructs a new HorizontalScrollView and wires up all internal elements and callbacks.
        /// </summary>
        public HorizontalScrollView()
        {
            AddToClassList(ussClassName);

            // Viewport clips content horizontally
            m_Viewport = new VisualElement { name = "viewport" };
            m_Viewport.AddToClassList(viewportUssClassName);
            hierarchy.Add(m_Viewport);

            // Content container – children are laid out in a row
            m_ContentContainer = new VisualElement { name = "content-container" };
            m_ContentContainer.AddToClassList(contentContainerUssClassName);
            m_Viewport.Add(m_ContentContainer);

            // Scrollbar row: [ArrowLeft] [Track [Thumb]] [ArrowRight]
            m_Scrollbar = new VisualElement { name = "scrollbar" };
            m_Scrollbar.AddToClassList(scrollbarUssClassName);
            hierarchy.Add(m_Scrollbar);

            m_ArrowLeft = new VisualElement { name = "arrow-left" };
            m_ArrowLeft.AddToClassList(arrowLeftUssClassName);
            m_Scrollbar.Add(m_ArrowLeft);

            m_Track = new VisualElement { name = "track" };
            m_Track.AddToClassList(trackUssClassName);
            m_Scrollbar.Add(m_Track);

            m_Thumb = new VisualElement { name = "thumb" };
            m_Thumb.AddToClassList(thumbUssClassName);
            m_Track.Add(m_Thumb);

            m_ArrowRight = new VisualElement { name = "arrow-right" };
            m_ArrowRight.AddToClassList(arrowRightUssClassName);
            m_Scrollbar.Add(m_ArrowRight);

            // Arrow click handlers
            m_ArrowLeft.RegisterCallback<ClickEvent>(OnArrowLeftClicked);
            m_ArrowRight.RegisterCallback<ClickEvent>(OnArrowRightClicked);

            // Thumb drag
            m_Thumb.RegisterCallback<PointerDownEvent>(OnThumbPointerDown);
            m_Thumb.RegisterCallback<PointerMoveEvent>(OnThumbPointerMove);
            m_Thumb.RegisterCallback<PointerUpEvent>(OnThumbPointerUp);
            m_Thumb.RegisterCallback<PointerCaptureOutEvent>(OnThumbCaptureOut);

            // Content drag (with threshold so clicks pass through to children)
            m_Viewport.RegisterCallback<PointerDownEvent>(OnContentPointerDown);
            m_Viewport.RegisterCallback<PointerMoveEvent>(OnContentPointerMove);
            m_Viewport.RegisterCallback<PointerUpEvent>(OnContentPointerUp);
            m_Viewport.RegisterCallback<PointerCaptureOutEvent>(OnContentCaptureOut);

            // Track click to jump
            m_Track.RegisterCallback<PointerDownEvent>(OnTrackPointerDown);

            // Mouse wheel
            m_Viewport.RegisterCallback<WheelEvent>(OnWheel);

            // Recalculate on layout change
            m_Viewport.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            m_ContentContainer.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        /// <summary>
        /// Scrolls the view so that the given child element is visible.
        /// </summary>
        public void ScrollTo(VisualElement child)
        {
            if (child == null) return;

            // Schedule so layout is resolved
            schedule.Execute(() =>
            {
                RecalculateSizes();

                float childLeft = child.worldBound.x - m_Viewport.worldBound.x + m_ScrollOffset;
                float childRight = childLeft + child.resolvedStyle.width;

                if (childLeft < m_ScrollOffset)
                {
                    SetScrollOffset(childLeft);
                }
                else if (childRight > m_ScrollOffset + m_ViewportWidth)
                {
                    SetScrollOffset(childRight - m_ViewportWidth);
                }
            });
        }

        void OnArrowLeftClicked(ClickEvent evt)
        {
            SetScrollOffset(m_ScrollOffset - k_ScrollStep);
        }

        void OnArrowRightClicked(ClickEvent evt)
        {
            SetScrollOffset(m_ScrollOffset + k_ScrollStep);
        }

        void OnWheel(WheelEvent evt)
        {
            float delta = evt.delta.y * k_WheelMultiplier;
            SetScrollOffset(m_ScrollOffset + delta);
            evt.StopPropagation();
        }

        // ── Thumb Drag ──

        void OnThumbPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            m_IsDraggingThumb = true;
            m_ThumbDragStartX = evt.position.x;
            m_ThumbDragStartOffset = m_ScrollOffset;
            m_Thumb.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnThumbPointerMove(PointerMoveEvent evt)
        {
            if (!m_IsDraggingThumb) return;

            float trackWidth = m_Track.resolvedStyle.width;
            float thumbWidth = m_Thumb.resolvedStyle.width;
            float usableTrack = trackWidth - thumbWidth;
            if (usableTrack <= 0f) return;

            float maxScroll = MaxScrollOffset();
            if (maxScroll <= 0f) return;

            float pixelDelta = evt.position.x - m_ThumbDragStartX;
            float scrollDelta = (pixelDelta / usableTrack) * maxScroll;
            SetScrollOffset(m_ThumbDragStartOffset + scrollDelta);
            evt.StopPropagation();
        }

        void OnThumbPointerUp(PointerUpEvent evt)
        {
            if (!m_IsDraggingThumb) return;
            m_IsDraggingThumb = false;
            m_Thumb.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnThumbCaptureOut(PointerCaptureOutEvent evt)
        {
            m_IsDraggingThumb = false;
        }

        // ── Content Drag (threshold-based so clicks pass through) ──

        void OnContentPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            m_ContentDragPending = true;
            m_IsDraggingContent = false;
            m_ContentDragPointerId = evt.pointerId;
            m_ContentDragStartX = evt.position.x;
            m_ContentDragStartOffset = m_ScrollOffset;
            // Do NOT capture pointer or stop propagation yet — let clicks through
        }

        void OnContentPointerMove(PointerMoveEvent evt)
        {
            if (!m_ContentDragPending && !m_IsDraggingContent) return;

            if (m_ContentDragPending)
            {
                float distance = Mathf.Abs(evt.position.x - m_ContentDragStartX);
                if (distance < k_DragThreshold) return;

                // Threshold exceeded — start real drag
                m_ContentDragPending = false;
                m_IsDraggingContent = true;
                m_Viewport.CapturePointer(m_ContentDragPointerId);
            }

            if (m_IsDraggingContent)
            {
                float delta = m_ContentDragStartX - evt.position.x;
                SetScrollOffset(m_ContentDragStartOffset + delta);
                evt.StopPropagation();
            }
        }

        void OnContentPointerUp(PointerUpEvent evt)
        {
            if (m_IsDraggingContent)
            {
                m_IsDraggingContent = false;
                m_Viewport.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            }
            m_ContentDragPending = false;
        }

        void OnContentCaptureOut(PointerCaptureOutEvent evt)
        {
            m_IsDraggingContent = false;
            m_ContentDragPending = false;
        }

        // ── Track Click to Jump ──

        void OnTrackPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;

            float max = MaxScrollOffset();
            if (max <= 0f) return;

            float trackWidth = m_Track.resolvedStyle.width;
            float thumbWidth = m_Thumb.resolvedStyle.width;
            float clickX = evt.localPosition.x;

            // Center the thumb on the click position
            float usableTrack = trackWidth - thumbWidth;
            float thumbCenter = clickX - thumbWidth * 0.5f;
            float ratio = Mathf.Clamp01(thumbCenter / usableTrack);
            SetScrollOffset(ratio * max);
            evt.StopPropagation();
        }

        // ── Layout ──

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            RecalculateSizes();
            UpdateLayout();
        }

        void RecalculateSizes()
        {
            m_ViewportWidth = m_Viewport.resolvedStyle.width;
            m_ContentWidth = m_ContentContainer.resolvedStyle.width;
        }

        float MaxScrollOffset()
        {
            float max = m_ContentWidth - m_ViewportWidth;
            return max > 0f ? max : 0f;
        }

        void SetScrollOffset(float value)
        {
            float max = MaxScrollOffset();
            m_ScrollOffset = Mathf.Clamp(value, 0f, max);
            UpdateLayout();
        }

        void UpdateLayout()
        {
            // Translate content
            m_ContentContainer.style.translate = new Translate(-m_ScrollOffset, 0f);

            float max = MaxScrollOffset();
            if (max <= 0f)
            {
                // Everything fits – hide scrollbar
                m_Scrollbar.style.display = DisplayStyle.None;
                return;
            }

            m_Scrollbar.style.display = DisplayStyle.Flex;

            // Thumb sizing and position
            float trackWidth = m_Track.resolvedStyle.width;
            float ratio = m_ViewportWidth / m_ContentWidth;
            float thumbWidth = Mathf.Max(trackWidth * ratio, 40f);
            float usableTrack = trackWidth - thumbWidth;
            float thumbLeft = (m_ScrollOffset / max) * usableTrack;

            m_Thumb.style.width = thumbWidth;
            m_Thumb.style.left = thumbLeft;
        }

    }
}
