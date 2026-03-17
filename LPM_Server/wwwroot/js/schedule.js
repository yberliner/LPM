window.ScheduleCalendar = (function () {
    let _cal    = null;
    let _dotnet = null;

    return {
        init(elementId, dotnetRef, initialView) {
            if (_cal) { try { _cal.destroy(); } catch (e) { } _cal = null; }
            _dotnet = dotnetRef;

            const el = document.getElementById(elementId);
            if (!el) return;

            _cal = new FullCalendar.Calendar(el, {
                initialView: initialView || 'timeGridWeek',
                height: 'auto',
                nowIndicator: true,
                headerToolbar: {
                    left: 'prev,next today',
                    center: 'title',
                    right: 'dayGridMonth,timeGridWeek,timeGridDay'
                },
                buttonText: { today: 'Today', month: 'Month', week: 'Week', day: 'Day' },
                eventTimeFormat:  { hour: '2-digit', minute: '2-digit', hour12: false },
                slotLabelFormat:  { hour: '2-digit', minute: '2-digit', hour12: false },
                slotMinTime:   '09:00:00',
                slotMaxTime:   '19:00:00',
                slotDuration:  '00:30:00',
                allDaySlot:    false,
                expandRows:    true,

                events: function (fetchInfo, successCallback, failureCallback) {
                    _dotnet.invokeMethodAsync('FetchEvents', fetchInfo.startStr, fetchInfo.endStr)
                        .then(events => successCallback(events))
                        .catch(e => { console.error('[ScheduleCalendar] FetchEvents error:', e); failureCallback(e); });
                },

                dateClick: function (info) {
                    _dotnet.invokeMethodAsync('OnDateClick', info.dateStr);
                },

                // Single left-click → open edit modal
                eventClick: function (info) {
                    info.jsEvent.preventDefault();
                    info.jsEvent.stopPropagation();
                    const mid = info.event.extendedProps.meetingId;
                    if (mid) _dotnet.invokeMethodAsync('OnEventDblClick', mid);
                },

                eventDidMount: function (info) {
                    const mid = info.event.extendedProps.meetingId;
                    if (mid) info.el.dataset.meetingId = String(mid);
                    info.el.style.cursor = 'pointer';

                    if (info.event.extendedProps.isWeekly) {
                        const titleEl = info.el.querySelector('.fc-event-title');
                        if (titleEl) {
                            const span = document.createElement('span');
                            span.textContent = ' ↻';
                            span.title = 'Repeats weekly';
                            span.style.cssText = 'opacity:.8;font-size:.9em;margin-left:3px;';
                            titleEl.appendChild(span);
                        }
                    }
                },

                eventMouseEnter: function (info) {
                    const ep = info.event.extendedProps;
                    const parts = [ep.pcName];
                    if (ep.auditorName) parts.push('Auditor: ' + ep.auditorName);
                    if (ep.durationMin)  parts.push(ep.durationMin + ' min');
                    if (ep.isWeekly)     parts.push('↻ Weekly');
                    info.el.title = parts.join('\n');
                }
            });

            _cal.render();
        },

        // Called from C# after FetchEvents to expand slot range if meetings fall outside 09–19
        updateSlotRange(minTime, maxTime) {
            if (!_cal) return;
            if (_cal.getOption('slotMinTime') !== minTime) _cal.setOption('slotMinTime', minTime);
            if (_cal.getOption('slotMaxTime') !== maxTime) _cal.setOption('slotMaxTime', maxTime);
        },

        // Used by Blazor @oncontextmenu — finds which FC event is at the given viewport coords
        getMeetingIdAt(x, y) {
            const el = document.elementFromPoint(x, y);
            if (!el) return 0;
            const eventEl = el.closest('[data-meeting-id]');
            return eventEl ? (parseInt(eventEl.dataset.meetingId) || 0) : 0;
        },

        refresh() { if (_cal) _cal.refetchEvents(); },

        destroy() {
            if (_cal) { try { _cal.destroy(); } catch (e) { } _cal = null; }
            _dotnet = null;
        }
    };
})();
