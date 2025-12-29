using System;

namespace WindBoard.Core.Ink
{
    public sealed class SimulatedPressure
    {
        private readonly SimulatedPressureParameters _p;
        private double _lowSpeedSeconds;
        private float _current;

        public SimulatedPressure(SimulatedPressureParameters parameters)
        {
            _p = parameters;
            Reset();
        }

        public float Current => _current;

        public void Reset()
        {
            _lowSpeedSeconds = 0;
            _current = Clamp01(_p.PressureNominal);
        }

        public float Update(double speedMmPerSec, double dtSec)
        {
            if (double.IsNaN(dtSec) || double.IsInfinity(dtSec) || dtSec <= 0)
            {
                dtSec = 0.016;
            }
            speedMmPerSec = SanitizeSpeed(speedMmPerSec);

            if (speedMmPerSec < _p.VStopMmPerSec)
            {
                _lowSpeedSeconds += dtSec;
            }
            else
            {
                _lowSpeedSeconds = 0;
            }

            bool stopped = _p.StopHoldMs > 0 && (_lowSpeedSeconds * 1000.0) >= _p.StopHoldMs;
            float target = stopped ? _p.PressureMax : MapSpeedToPressure(speedMmPerSec);

            _current = SmoothApproach(_current, target, dtSec);
            return _current;
        }

        public float Finish()
        {
            _current = Clamp01(_p.PressureEnd);
            return _current;
        }

        private float MapSpeedToPressure(double speedMmPerSec)
        {
            double denom = _p.VFastMmPerSec - _p.VSlowMmPerSec;
            double t = denom <= 0.0001 ? 1.0 : (speedMmPerSec - _p.VSlowMmPerSec) / denom;
            t = Math.Clamp(t, 0.0, 1.0);
            t = t * t * (3.0 - 2.0 * t); // Smoothstep

            float pressure = (float)(_p.PressureMax + (_p.PressureMin - _p.PressureMax) * t);
            float min = Math.Min(_p.PressureMin, _p.PressureMax);
            float max = Math.Max(_p.PressureMin, _p.PressureMax);
            return Math.Clamp(pressure, min, max);
        }

        private float SmoothApproach(float current, float target, double dtSec)
        {
            current = Clamp01(current);
            target = Clamp01(target);

            int ms = target > current ? _p.AttackMs : _p.ReleaseMs;
            if (ms <= 0)
            {
                return target;
            }

            double tau = ms / 1000.0;
            double alpha = dtSec / (dtSec + tau);
            alpha = Math.Clamp(alpha, 0.0, 1.0);
            float next = (float)(current + (target - current) * alpha);
            return Clamp01(next);
        }

        private static double SanitizeSpeed(double speedMmPerSec)
        {
            if (double.IsNaN(speedMmPerSec) || double.IsInfinity(speedMmPerSec) || speedMmPerSec < 0)
            {
                return 0;
            }
            return speedMmPerSec;
        }

        private static float Clamp01(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Clamp(value, 0f, 1f);
        }
    }
}
