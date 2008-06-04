/* 
 * Copyright (C) 2007 Sasa Coh <sasacoh@gmail.com>
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA 
 * 
 * @see http://voipengine.googlepages.com/
 *  
 */

using System;
using Sipek.Common;

namespace Sipek.Common.CallControl
{

  /// <summary>
  /// CStateMachine class is a telephony data container for signle call. It maintains call state, 
  /// communicates with signaling via proxy and informs about events from signaling.
  /// A Finite State Machine is implemented in State design pattern!
  /// </summary>
  public class CStateMachine : IStateMachine
  {
    #region Variables

    private IAbstractState _state;
    // State instances....
    private CIdleState _stateIdle;
    private CConnectingState _stateCalling;
    private CAlertingState _stateAlerting;
    private CActiveState _stateActive;
    private CReleasedState _stateReleased;
    private CIncomingState _stateIncoming;
    private CHoldingState _stateHolding;
    // call properties
    private ECallType _callType ;
    private TimeSpan _duration;
    private DateTime _timestamp;
    private CCallManager _manager;
    protected ITimer _noreplyTimer;
    protected ITimer _releasedTimer;
    private int _session = -1;
    private ICallProxyInterface _sigProxy;
    private string _callingNumber = "";
    private string _callingName = "";
    private bool _incoming = false;
    private bool _isHeld = false;
    private bool _is3Pty = false;
    private bool _counting = false; // if duration counter is started
    private bool _holdRequested = false;
    private bool _retrieveRequested = false;

    #endregion Variables

    #region Properties
    /// <summary>
    /// A reference to CCallManager instance
    /// </summary>
    public CCallManager Manager
    {
      get { return _manager; }
    }

    /// <summary>
    /// Call/Session identification
    /// </summary>
    public override int Session
    {
      get { return _session; }
      set 
      { 
        _session = value;
        // don't forget to set proxy sessionId in case of incoming call!
        this.CallProxy.SessionId = value;
      }
    }

    /// <summary>
    /// Calling number property
    /// </summary>
    public override string CallingNumber
    {
      get { return _callingNumber; }
      set { _callingNumber = value; }
    }

    /// <summary>
    /// Calling name property
    /// </summary>
    public override string CallingName
    {
      get { return _callingName; }
      set { _callingName = value; }
    }

    /// <summary>
    /// Incoming call flag
    /// </summary>
    public override bool Incoming
    {
      get { return _incoming; }
      set { _incoming = value; }
    }

    /// <summary>
    /// Is call held by other side
    /// </summary>
    public override bool IsHeld
    {
      get { return _isHeld; }
      set { _isHeld = value; }
    }

    /// <summary>
    /// Is this call 3pty (==2 active sessions)
    /// </summary>
    public override bool Is3Pty
    {
      get { return _is3Pty; }
      set { _is3Pty = value; }
    }
    
    /// <summary>
    /// 
    /// </summary>
    public override EStateId StateId
    {
      get { return _state.Id; }
    }


    /// <summary>
    /// ???
    /// </summary>
    internal override bool Counting
    {
      get { return _counting; }
      set { _counting = value; }
    }

    /// <summary>
    /// Duration of a call
    /// </summary>
    public override TimeSpan Duration
    {
      set { _duration = value; }
      get { return _duration; }
    }

    /// <summary>
    /// Calculate call duration
    /// </summary>
    public override TimeSpan RuntimeDuration
    {
      get
      {
        if (true == Counting)
        {
          return DateTime.Now.Subtract(Time);
        }
        return TimeSpan.Zero;
      }
    }

    /// <summary>
    /// Current State of the state machine
    /// </summary>
    internal override IAbstractState State
    {
      get { return _state; }
    }

    /// <summary>
    /// Check for null state machine
    /// </summary>
    public override bool IsNull
    {
      get { return false; }
    }

    /// <summary>
    /// Signaling proxy instance (seperately created for each call)
    /// </summary>
    internal override ICallProxyInterface CallProxy
    {
      get { return _sigProxy; } 
    }

    /// <summary>
    /// Media proxy instance getter for handling tones
    /// </summary>
    internal override IMediaProxyInterface MediaProxy
    {
      get { return _manager.MediaProxy; }
    }

    /// <summary>
    /// Call type property for Call log
    /// </summary>
    internal override ECallType Type
    {
      get { return _callType; }
      set { _callType = value; }
    }

    /// <summary>
    /// Timestamp of a call
    /// </summary>
    internal override DateTime Time
    {
      set { _timestamp = value; }
      get { return _timestamp; }
    }

    /// <summary>
    /// Has been call hold requested
    /// </summary>
    internal override bool HoldRequested
    {
      get { return _holdRequested; }
      set { _holdRequested = value;  }
    }

    /// <summary>
    /// Has been call retrieve requested
    /// </summary>
    internal override bool RetrieveRequested
    {
      get { return _retrieveRequested; }
      set { _retrieveRequested = value; }
    }
    
    /// <summary>
    /// Data access instance
    /// </summary>
    internal override IConfiguratorInterface Config
    {
      get { return _manager.Config;  }
    }

    /// <summary>
    /// Call log instance
    /// </summary>
    protected ICallLogInterface CallLoger
    {
      get { return _manager.CallLogger; }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Call/Session constructor. Initializes call states, creates signaling proxy, initialize time,
    /// initialize timers.
    /// </summary>
    /// <param name="manager">reference to call manager</param>
    public CStateMachine(CCallManager manager)
    {
      // store manager reference...
      _manager = manager;

      // create call proxy
      _sigProxy = _manager.StackProxy.createCallProxy();

      // initialize call states
      _stateIdle = new CIdleState(this);
      _stateAlerting = new CAlertingState(this);
      _stateActive = new CActiveState(this);
      _stateCalling = new CConnectingState(this);
      _stateReleased = new CReleasedState(this);
      _stateIncoming = new CIncomingState(this);
      _stateHolding = new CHoldingState(this);
      // change state
      _state = _stateIdle;
      
      // initialize data
      Time = DateTime.Now;
      Duration = TimeSpan.Zero;

      // Initialize timers
      if (null != _manager)
      { 
        _noreplyTimer = _manager.Factory.createTimer();
        _noreplyTimer.Interval = 15000; // hardcoded to 15s
        _noreplyTimer.Elapsed = new TimerExpiredCallback(_noreplyTimer_Elapsed);

        _releasedTimer = _manager.Factory.createTimer();
        _releasedTimer.Interval = 5000; // hardcoded to 15s
        _releasedTimer.Elapsed = new TimerExpiredCallback(_releasedTimer_Elapsed);
      }
    }

    #endregion Constructor

    #region Private Methods
    /// <summary>
    /// Change state
    /// </summary>
    /// <param name="state">instance of state to change to</param>
    private  void changeState(IAbstractState state)
    {
      _state.onExit();
      _state = state;
      _state.onEntry();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void _noreplyTimer_Elapsed(object sender, EventArgs e)
    {
      State.noReplyTimerExpired(this.Session);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void _releasedTimer_Elapsed(object sender, EventArgs e)
    {
      State.releasedTimerExpired(this.Session);
    }

    ///////////////////////////////////////////////////////////////////////////////////
    // Timers
    /// <summary>
    /// Start timer by timer type
    /// </summary>
    /// <param name="ttype">timer type</param>
    internal override bool startTimer(ETimerType ttype)
    {
      bool success = false;
      switch (ttype)
      {
        case ETimerType.ENOREPLY:
          success = _noreplyTimer.Start();
          break;
        case ETimerType.ERELEASED:
          success = _releasedTimer.Start();
          break;
      }
      return success;
    }

    /// <summary>
    /// Stop timer by timer type
    /// </summary>
    /// <param name="ttype">timer type</param>
    internal override bool stopTimer(ETimerType ttype)
    {
      bool success = false;
      switch (ttype)
      {
        case ETimerType.ENOREPLY:
          success = _noreplyTimer.Stop();
          break;
        case ETimerType.ERELEASED:
          success = _releasedTimer.Stop();
          break;
      }
      return success;
    }

    /// <summary>
    /// Stop all timer...
    /// </summary>
    internal override void stopAllTimers()
    {
      _noreplyTimer.Stop();
      _releasedTimer.Stop();
    }

    /// <summary>
    /// Run queued requests
    /// </summary>
    internal override void activatePendingAction()
    {
      Manager.activatePendingAction();
    }
    #endregion

    #region Public Methods

    /// <summary>
    /// Change state by state id
    /// </summary>
    /// <param name="stateId">state id</param>
    public override void changeState(EStateId stateId)
    {
      switch (stateId) 
      {
        case EStateId.IDLE:  changeState(_stateIdle); break;
        case EStateId.CONNECTING: changeState(_stateCalling); break;
        case EStateId.ALERTING: changeState(_stateAlerting); break;
        case EStateId.ACTIVE: changeState(_stateActive); break;
        case EStateId.RELEASED: changeState(_stateReleased); break;
        case EStateId.INCOMING: changeState(_stateIncoming); break;
        case EStateId.HOLDING: changeState(_stateHolding); break;
      }
      // inform manager 
      if ((null != _manager)&&(Session != -1)) _manager.updateGui(this.Session);
    }

    /// <summary>
    /// Destroy call. Calculate call duraton time, edit call log, destroy session.
    /// </summary>
    public override void destroy()
    {
      // stop tones
      MediaProxy.stopTone();
      // Calculate timing
      if (true == Counting)
      {
        Duration = DateTime.Now.Subtract(Time);
      }

      // update call log
      if (((Type != ECallType.EDialed) || (CallingNumber.Length > 0)) && (Type != ECallType.EUndefined))
      {
        CallLoger.addCall(Type, CallingNumber, CallingName, Time, Duration);
        CallLoger.save();
      } 
      // reset data
      CallingNumber = "";
      Incoming = false;
      changeState(EStateId.IDLE);
      if (null != _manager) _manager.destroySession(Session);
    }

    #endregion Methods
  }

} // namespace Sipek.Common.CallControl
